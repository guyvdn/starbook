using Bookmarks.Api;

// Docker HEALTHCHECK: the runtime image has no shell or curl, so the binary probes itself.
if (args is ["--healthcheck", ..])
{
    var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';')[0] ?? "8080";
    using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    try
    {
        return (await probe.GetAsync($"http://127.0.0.1:{port}/healthz")).IsSuccessStatusCode ? 0 : 1;
    }
    catch
    {
        return 1;
    }
}

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));

var dataPath = Path.GetFullPath(builder.Configuration["DataPath"] ?? "data", builder.Environment.ContentRootPath);
builder.Services.AddSingleton(await SpaceStore.OpenAsync(dataPath));
builder.Services.AddSingleton<StartPage>();
builder.Services.AddSingleton(new ImageStore(dataPath));
builder.Services.AddTransient<MissingIcons>(); // IconFetcher is a typed HttpClient: transient
builder.Services.AddHttpClient<IconFetcher>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(8);
        // Some sites refuse requests that do not look like a browser.
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,image/*,*/*;q=0.8");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        MaxAutomaticRedirections = 5,
        ConnectTimeout = TimeSpan.FromSeconds(5),
    });

var app = builder.Build();

app.Logger.LogInformation("Data path: {DataPath}", dataPath);

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Vite fingerprints everything under /assets, so it can be cached forever.
        if (ctx.Context.Request.Path.StartsWithSegments("/assets"))
            ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    },
});

// Healthy when the data folder loaded and is writable.
app.MapGet("/healthz", (SpaceStore store) =>
{
    try
    {
        var probe = Path.Combine(dataPath, ".healthz");
        File.WriteAllText(probe, "ok");
        File.Delete(probe);
    }
    catch (IOException)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
    return store.FirstSlug() is null ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable) : Results.Text("ok");
});

app.MapGet("/", (SpaceStore store) => Results.Redirect($"/s/{store.FirstSlug()}"));

app.MapGet("/s/{slug}", (string slug, SpaceStore store, StartPage page, HttpContext http) =>
{
    if (store.Get(slug) is not { } space) return Results.NotFound();
    http.Response.Headers.CacheControl = "no-cache";
    return page.Render(new StartPageData(space, store.List()));
});

var api = app.MapGroup("/api/spaces");

api.MapGet("/", (SpaceStore store) => Results.Ok(store.List()));

api.MapGet("/{slug}", (string slug, SpaceStore store) =>
    store.Get(slug) is { } space ? Results.Ok(space) : Results.NotFound());

api.MapPut("/{slug}", async (string slug, Space space, SpaceStore store, CancellationToken ct) =>
{
    if (space.Slug != slug) return Results.BadRequest(new ErrorResponse("Slug in body does not match the URL."));
    if (SpaceValidator.Validate(space) is { } error) return Results.BadRequest(new ErrorResponse(error));

    var result = await store.SaveAsync(space, ct);
    return result.Status switch
    {
        SaveStatus.Saved => Results.Ok(result.Space),
        SaveStatus.Conflict => Results.Conflict(result.Space),
        _ => Results.NotFound(),
    };
});

api.MapPost("/{slug}/missing-icons", async (string slug, MissingIcons missing, CancellationToken ct) =>
    await missing.FillAsync(slug, ct) is { } space ? Results.Ok(space) : Results.NotFound());

api.MapPost("/", async (CreateSpaceRequest request, SpaceStore store, CancellationToken ct) =>
{
    if (!SpaceValidator.IsValidSlug(request.Slug))
        return Results.BadRequest(new ErrorResponse("Invalid slug: use lowercase letters, digits and hyphens."));
    if (string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest(new ErrorResponse("A space needs a name."));

    return await store.CreateAsync(request.Slug, request.Name.Trim(), ct) is { } space
        ? Results.Created($"/api/spaces/{space.Slug}", space)
        : Results.Conflict(new ErrorResponse($"Space '{request.Slug}' already exists."));
});

api.MapDelete("/{slug}", async (string slug, SpaceStore store, CancellationToken ct) =>
    await store.DeleteAsync(slug, ct) ? Results.NoContent() : Results.NotFound());

app.MapPost("/api/icons", async (IconRequest request, IconFetcher icons, HttpContext http) =>
{
    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var url)
        || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        return Results.BadRequest(new ErrorResponse("Give an http or https URL."));

    // Bound the whole lookup, not just each request: a site can offer many candidates.
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
    timeout.CancelAfter(TimeSpan.FromSeconds(20));
    try
    {
        return Results.Ok(await icons.FetchAsync(url, timeout.Token));
    }
    catch (OperationCanceledException) when (!http.RequestAborted.IsCancellationRequested)
    {
        return Results.Ok(new IconResult(null, null));
    }
});

// The body is the image itself (upload or paste); its format is sniffed, so the Content-Type does not matter.
app.MapPost("/api/images", async (HttpRequest request, ImageStore images, CancellationToken ct) =>
{
    using var buffer = new MemoryStream();
    var chunk = new byte[16 * 1024];
    int read;
    while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
    {
        if (buffer.Length + read > ImageStore.MaxBytes)
            return Results.BadRequest(new ErrorResponse("Images are limited to 1 MB."));
        buffer.Write(chunk, 0, read);
    }

    return await images.SaveAsync(buffer.ToArray(), ct) is { } image
        ? Results.Ok(new ImageResponse(image))
        : Results.BadRequest(new ErrorResponse("Not a PNG, JPEG, GIF, WebP, ICO or SVG image."));
});

app.MapGet("/img/{name}", (string name, ImageStore images, HttpContext http) =>
{
    var path = Path.Combine(images.Directory, name);
    if (!SpaceValidator.IsValidImage(name) || !File.Exists(path)) return Results.NotFound();

    var headers = http.Response.Headers;
    headers.CacheControl = "public, max-age=31536000, immutable"; // the name is the content hash
    headers.XContentTypeOptions = "nosniff";
    // An SVG opened directly must not run script on this origin.
    headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
    return Results.File(path, ImageStore.ContentType(Path.GetExtension(name)[1..]));
});

app.Run();
return 0;

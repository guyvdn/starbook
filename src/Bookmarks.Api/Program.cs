using Bookmarks.Api;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));

var dataPath = Path.GetFullPath(builder.Configuration["DataPath"] ?? "data", builder.Environment.ContentRootPath);
builder.Services.AddSingleton(await SpaceStore.OpenAsync(dataPath));
builder.Services.AddSingleton<StartPage>();

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

app.Run();

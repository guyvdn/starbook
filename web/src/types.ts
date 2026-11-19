// Mirrors src/Starbook.Api/Model.cs.

export interface Tile {
  id: string;
  title: string;
  url: string;
  image: string | null;
}

export interface Section {
  id: string;
  title: string | null;
  tiles: Tile[];
}

export interface Tab {
  id: string;
  title: string;
  sections: Section[];
}

export interface Space {
  slug: string;
  name: string;
  version: number;
  tabs: Tab[];
  /** One of PALETTE (theme.ts); absent for the default look. */
  color?: string | null;
}

export interface SpaceSummary {
  slug: string;
  name: string;
}

export interface StartPageData {
  space: Space;
  spaces: SpaceSummary[];
}

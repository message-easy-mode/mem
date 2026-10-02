# MDX → Markdown conversion report

## Result

- Source MDX files discovered: **16**
- Markdown files produced: **16**
- Converted Markdown size: **117,581 bytes**
- Generated at: `2026-07-06T08:11:19Z`
- Conversion version: `1.0`

## Supported source constructs converted

| Source construct | Markdown output | Count |
|---|---|---:|
| `<Callout title="..." variant="important">…</Callout>` | GitHub-style `> [!IMPORTANT]` alert, preserving title and body | 14 |
| `<BadgeList items={[...]} />` | `**Topics:**` line | 14 |
| `<YouTubeEmbed ... />` | Online-only YouTube watch link in a Markdown note | 1 |
| Raw `<a href="…">…</a>` links | Ordinary Markdown links | 13 |
| `/docs/...` links with a local target | Relative Markdown links | 87 |
| Placeholder angle-brackets in prose | Escaped text, for example `&lt;your-server&gt;` | 0 |

## Structural adjustments

- Each document keeps its original YAML frontmatter: `title`, `description`, `section`, and `order`.
- Each converted document has a document H1 inserted from its frontmatter `title`, so it reads properly when opened directly from disk.
- One document used body-level H1s. Its heading tree was shifted down one level so the new document title remains the only H1. Shell comments inside fenced code blocks were not changed.
- The output includes `metadata/navigation.json`, `metadata/manifest.json`, and `metadata/search-index.json` as import-ready starting artifacts for the MEM Docs feature.

## Source routes without a converted offline page

The supplied ZIP did not contain local source pages for these route families. They are retained as public-site links and will require connectivity:

`/get-started`, `/roadmap`, `/roadmap/0.1.1`, `/roadmap/0.1.2`

## Empty source Markdown files excluded

These files existed in the source ZIP but contained no content, so they were not added as documentation pages:

- `content/faq/faq.md`
- `content/install/advanced-config.md`
- `content/install/quick-start.md`
- `content/trust/operator-principles.md`

## Assets

No image assets or Markdown image references were present in the supplied documentation source. No `assets/` directory was generated.

## Accuracy review still required

This is a format conversion, not a product-content rewrite. The supplied source includes earlier deployment-era material, including references to `NextAuth`, `JWT`, `mem-deploy`, legacy `.env` settings, Nginx Proxy Manager workflows, and v0.1.0 release material.

Before integrating into the v0.1.1 in-product docs reader, review and update the content for current MEM behavior—particularly named operators, TOTP/recovery, step-up operations, Backup Catalog, restore workspaces, private staging, production restore safeguards, diagnostics, CLI, and localisation.

## Validation performed

- Every MDX file with content was converted.
- YAML frontmatter required by the source docs was present on every converted document.
- No unresolved `Callout`, `BadgeList`, `YouTubeEmbed`, or raw `<a>` tags remain outside fenced code.
- All source `/docs/...` links that correspond to a converted page were rewritten to relative file links.
- SHA-256 checksums were generated for the package files.

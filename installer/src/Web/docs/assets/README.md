# Documentation assets

Portable screenshots and diagrams used by MEM documentation live here.

Supported release formats are:

- PNG (`.png`)
- JPEG (`.jpg` / `.jpeg`)
- WebP (`.webp`)

SVG and other active/document formats are intentionally not part of the MEM 0.2.0 portable asset contract.

## Authoring rule

Use normal Markdown image syntax with **non-empty alt text** and a **relative path** from the canonical source document to this `docs/assets/` tree. Do not use remote image URLs, absolute paths, data URLs, inline image titles, or spaces in asset filenames.

Example from `docs/en/installation/example.md` or `docs/de/installation/example.md`:

```markdown
![DigitalOcean firewall showing the MEM public ports](../../assets/installation/digitalocean/firewall.png)
```

During `docs:build`, MEM validates the image, copies it into `release-pack/assets/`, and rewrites the generated Markdown to the correct relative path for that document's portable output location. This matters because English and German output paths have different directory depths.

The downloaded language ZIPs retain the same `docs/` + `assets/` relationship, so when a user extracts a documentation ZIP and opens a Markdown file locally, compatible Markdown viewers can resolve the bundled images without an Internet connection.

Every packaged asset must be referenced by at least one document; unreferenced assets fail `docs:check`/`docs:build` rather than accumulating silently.

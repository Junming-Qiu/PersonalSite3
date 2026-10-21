# Personal Site

.NET 8 MVC site: About, Blog, and a photography Gallery. Terms are defined in `CONTEXT.md`.

## Run the site

```
dotnet run --project backend
```

## Gallery

Photos are published as stripped, hash-keyed JPEGs (thumb, med, full) in a DigitalOcean Spaces bucket. Metadata lives in `gallery.db` at the repo root (gitignored). The site reads it read-only; a missing file means an empty Gallery.

### Setup

Create `.env` at the repo root (gitignored):

```
BUCKET_WRITE_KEY=...          # Spaces secret
BUCKET_ACCESS_KEY_ID=...
BUCKET_ENDPOINT="https://<bucket>.<region>.digitaloceanspaces.com"
BUCKET_CDN_ENDPOINT="https://<bucket>.<region>.cdn.digitaloceanspaces.com"
```

Requires `ffmpeg` on the PATH. The CDN base the site uses is `Gallery:CdnBase` in `backend/appsettings.json`.

### Ingest and Editor (one tool)

```
dotnet run --project tools/GalleryAdmin
```

Opens at http://localhost:5100.

**Add photos:** click "Add photos…", then Browse… (native macOS folder picker) or type a path, and Scan. Each file is validated; unusable ones are greyed with the reason (not JPEG, unreadable, ratio not 3:2, 4:3 or 1:1 (within 1%), already ingested, duplicate in the folder). Tick the rest and click Ingest. It reads the capture date, strips metadata, makes the three renditions, uploads them, and adds a row to `gallery.db`. Originals never leave your machine. The newly added frames are selected when it finishes, ready to label.

**Label and delete:** click to select, shift-click for a range, cmd/ctrl-click to toggle, or use the checkboxes. Select all / Select none work on the visible frames. Edit date, location, film stock, push/pull and description; edits save when you click off a field and a toast confirms. Date and description are single-frame only. "Needs attention only" shows frames missing a date or location. Rotate (↺ ↻) turns the selected frames 90° by re-uploading them (they get new ids; labels are kept). Delete removes the selected frames from the site and the bucket after a confirmation.

### Not built yet

- Invisible watermark (only a JPEG comment with the owner's name is embedded).
- Copying `gallery.db` to the host.

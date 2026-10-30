# Branding assets

Brand assets for the **Whisperer** agent, derived from `app/web/public/icon.svg`
(teal gradient `#18bda0 → #0a565f`, gold `#e7b24c`, cream `#f4ecd6`).

## AWS End User Messaging — RCS agent

| File | Size | Use |
| --- | --- | --- |
| `rcs-banner.png` / `rcs-banner.jpg` | 1440×448 | RCS agent **banner** (hero image) |
| `rcs-logo.png` | 224×224 | RCS agent **logo/icon** (transparent corners — keep as PNG) |

`rcs-banner.src.html` is the self-contained source for the banner. Regenerate the
PNG from it with headless Chrome (no extra tooling required):

```bash
"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" \
  --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 \
  --window-size=1440,448 --default-background-color=00000000 \
  --screenshot=rcs-banner.png branding/rcs-banner.src.html
# JPEG variant:
sips -s format jpeg -s formatOptions 90 rcs-banner.png --out rcs-banner.jpg
```

The 224×224 logo is rendered the same way from `app/web/public/icon.svg` at a
224×224 window size.

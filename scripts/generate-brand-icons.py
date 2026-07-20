"""Generate branding PNG/ICO assets from the LunarQ source icon.

Removes the solid navy plate and white exterior so outputs have a
transparent background around the LQ monogram.
"""

from __future__ import annotations

from collections import deque
from pathlib import Path

from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
SOURCE = Path(
    r"C:\Users\bagib\.cursor\projects\d-Dev-LunarQ-mcp-track-tokens\assets"
    r"\c__Users_bagib_AppData_Roaming_Cursor_User_workspaceStorage_"
    r"d1eee9d29b2c8e9429495489f734de0e_images_Icon-3d3e7d1d-ca16-403a-85b4-5046b2166df1.png"
)
# Prefer a repo-local copy if present.
LOCAL_SOURCE = ROOT / "assets" / "branding" / "lunarqu-icon-source.png"

# Approximate navy fill of the original rounded-square plate.
_PLATE_BG = (5, 11, 25)


def ensure_source() -> Path:
    LOCAL_SOURCE.parent.mkdir(parents=True, exist_ok=True)
    if not LOCAL_SOURCE.exists():
        if not SOURCE.exists():
            raise SystemExit(f"Source icon not found: {SOURCE}")
        LOCAL_SOURCE.write_bytes(SOURCE.read_bytes())
    return LOCAL_SOURCE


def _color_dist(c: tuple[int, int, int], ref: tuple[int, int, int]) -> float:
    return (
        (c[0] - ref[0]) ** 2 + (c[1] - ref[1]) ** 2 + (c[2] - ref[2]) ** 2
    ) ** 0.5


def _is_white(c: tuple[int, int, int]) -> bool:
    return c[0] > 240 and c[1] > 240 and c[2] > 240


def remove_background(img: Image.Image) -> Image.Image:
    """Make the navy plate and white exterior transparent; keep the LQ mark."""
    img = img.convert("RGBA")
    w, h = img.size
    px = img.load()
    bg = _PLATE_BG

    bg_mask = Image.new("L", (w, h), 0)
    bm = bg_mask.load()
    for y in range(h):
        for x in range(w):
            r, g, b, _a = px[x, y]
            dist = _color_dist((r, g, b), bg)
            lum = 0.299 * r + 0.587 * g + 0.114 * b
            if _is_white((r, g, b)) or dist < 22 or (dist < 40 and lum < 28):
                bm[x, y] = 255

    seeds = [
        (0, 0),
        (w - 1, 0),
        (0, h - 1),
        (w - 1, h - 1),
        (w // 2, 0),
        (0, h // 2),
        (w - 1, h // 2),
        (w // 2, h - 1),
        (180, 180),
        (844, 180),
        (180, 844),
        (844, 844),
        (512, 120),
        (120, 512),
        (900, 512),
        (512, 900),
        (300, 300),
        (700, 200),
        (200, 700),
        (400, 150),
        (150, 400),
    ]
    flood = Image.new("L", (w, h), 0)
    fp = flood.load()
    queue: deque[tuple[int, int]] = deque()
    seen: set[tuple[int, int]] = set()
    for sx, sy in seeds:
        if not (0 <= sx < w and 0 <= sy < h):
            continue
        if bm[sx, sy] > 0 and (sx, sy) not in seen:
            queue.append((sx, sy))
            seen.add((sx, sy))
            fp[sx, sy] = 255
    while queue:
        x, y = queue.popleft()
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if (nx, ny) in seen or not (0 <= nx < w and 0 <= ny < h):
                continue
            if bm[nx, ny] > 0:
                seen.add((nx, ny))
                queue.append((nx, ny))
                fp[nx, ny] = 255

    # Slight dilate to eat anti-aliased plate fringe without eating the logo.
    flood_dilated = flood.filter(ImageFilter.MaxFilter(3))
    fp0 = flood.load()
    fp2 = flood_dilated.load()
    for y in range(h):
        for x in range(w):
            if fp2[x, y] and not fp0[x, y]:
                r, g, b, _a = px[x, y]
                lum = 0.299 * r + 0.587 * g + 0.114 * b
                if lum > 40 and not _is_white((r, g, b)):
                    fp2[x, y] = 0

    alpha = Image.new("L", (w, h), 255)
    ap = alpha.load()
    for y in range(h):
        for x in range(w):
            if fp2[x, y]:
                ap[x, y] = 0

    for y in range(h):
        for x in range(w):
            if ap[x, y] == 0:
                continue
            r, g, b, _a = px[x, y]
            dist = _color_dist((r, g, b), bg)
            lum = 0.299 * r + 0.587 * g + 0.114 * b
            if lum < 45 and dist < 55:
                t = max(dist / 55.0, lum / 45.0)
                ap[x, y] = int(255 * min(1.0, t))

    # Keep dense logo components; drop sparse leftover squircle-ring fringe.
    visited = [[False] * w for _ in range(h)]
    keep: set[tuple[int, int]] = set()
    for y in range(h):
        for x in range(w):
            if visited[y][x] or ap[x, y] < 12:
                visited[y][x] = True
                continue
            comp: list[tuple[int, int]] = []
            queue = deque([(x, y)])
            visited[y][x] = True
            min_x = max_x = x
            min_y = max_y = y
            while queue:
                cx, cy = queue.popleft()
                comp.append((cx, cy))
                min_x = min(min_x, cx)
                max_x = max(max_x, cx)
                min_y = min(min_y, cy)
                max_y = max(max_y, cy)
                for nx, ny in (
                    (cx + 1, cy),
                    (cx - 1, cy),
                    (cx, cy + 1),
                    (cx, cy - 1),
                    (cx + 1, cy + 1),
                    (cx - 1, cy - 1),
                    (cx + 1, cy - 1),
                    (cx - 1, cy + 1),
                ):
                    if 0 <= nx < w and 0 <= ny < h and not visited[ny][nx]:
                        visited[ny][nx] = True
                        if ap[nx, ny] >= 12:
                            queue.append((nx, ny))
            bw = max_x - min_x + 1
            bh = max_y - min_y + 1
            fill = len(comp) / (bw * bh)
            if len(comp) >= 2000 and fill >= 0.05:
                keep.update(comp)

    halo: set[tuple[int, int]] = set()
    for x, y in keep:
        for nx in range(x - 2, x + 3):
            for ny in range(y - 2, y + 3):
                if 0 <= nx < w and 0 <= ny < h and (nx, ny) not in keep:
                    halo.add((nx, ny))

    new_alpha = Image.new("L", (w, h), 0)
    nap = new_alpha.load()
    for x, y in keep:
        nap[x, y] = ap[x, y]
    for x, y in halo:
        if ap[x, y] > 0:
            nap[x, y] = min(ap[x, y], 120)

    new_alpha = new_alpha.filter(ImageFilter.GaussianBlur(radius=0.4))
    nap = new_alpha.load()

    out = img.copy()
    out.putalpha(new_alpha)
    op = out.load()
    for y in range(h):
        for x in range(w):
            a = nap[x, y]
            if a < 8:
                op[x, y] = (0, 0, 0, 0)
            else:
                r, g, b, _ = op[x, y]
                op[x, y] = (r, g, b, a)
    return out


def save_png(img: Image.Image, path: Path, size: int) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    resized = img.resize((size, size), Image.Resampling.LANCZOS)
    if resized.mode != "RGBA":
        resized = resized.convert("RGBA")
    resized.save(path, format="PNG")
    print(f"wrote {path} ({size}x{size})")


def save_ico(img: Image.Image, path: Path, sizes: list[int]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if img.mode != "RGBA":
        img = img.convert("RGBA")
    # Pillow writes a multi-size ICO when sizes= is provided.
    img.save(
        path,
        format="ICO",
        sizes=[(s, s) for s in sizes],
    )
    print(f"wrote {path} sizes={sizes}")


def main() -> None:
    source_path = ensure_source()
    img = remove_background(Image.open(source_path))

    branding = ROOT / "assets" / "branding"
    dashboard_public = ROOT / "src" / "McpTrackTokens.Dashboard" / "public"
    tray_assets = ROOT / "src" / "McpTrackTokens.Tray" / "Assets"
    desktop_assets = ROOT / "src" / "McpTrackTokens.Desktop" / "Assets"
    setup_assets = ROOT / "setup" / "McpTrackTokens.Tray.Setup" / "assets"

    # Master + common PNG sizes
    save_png(img, branding / "lunarqu-icon.png", 1024)
    save_png(img, branding / "lunarqu-icon-512.png", 512)
    save_png(img, branding / "lunarqu-icon-256.png", 256)
    save_png(img, branding / "lunarqu-icon-128.png", 128)
    save_png(img, branding / "lunarqu-icon-64.png", 64)
    save_png(img, branding / "lunarqu-icon-32.png", 32)

    # Dashboard web assets
    save_png(img, dashboard_public / "brand-icon.png", 128)
    save_png(img, dashboard_public / "apple-touch-icon.png", 180)
    save_png(img, dashboard_public / "icon-192.png", 192)
    save_png(img, dashboard_public / "icon-512.png", 512)

    ico_sizes = [16, 24, 32, 48, 64, 128, 256]
    save_ico(img, branding / "app.ico", ico_sizes)
    save_ico(img, dashboard_public / "favicon.ico", [16, 32, 48])
    save_ico(img, tray_assets / "app.ico", ico_sizes)
    save_ico(img, desktop_assets / "app.ico", ico_sizes)
    save_ico(img, setup_assets / "app.ico", ico_sizes)


if __name__ == "__main__":
    main()

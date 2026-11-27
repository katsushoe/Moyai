"""Moyai アイコン生成スクリプト（結び目 / 太い黒縁 / 白塗り / 透明背景）

使い方: python make_icon.py
出力:   out/ 以下に SVG, 各サイズ PNG, moyai.ico, moyai_tray.ico, プレビュー画像
"""
import math
import os
import sys
import numpy as np
from PIL import Image

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
DESIGN = 512  # 設計座標系

# 三つの交差で上下が交互になる結び目。上側を開いて左右へロープの端を伸ばす。
# 小サイズも同じ輪郭にし、アプリと通知領域で形を統一する。
def knot_rope():
    points = [(96, 76, 3)]
    count = 72
    for index in range(count + 1):
        t = 0.38 + (2 * math.pi - 0.76) * index / count
        x = 256 + 70 * (math.sin(t) + 2 * math.sin(2 * t))
        y = 245 + 70 * (math.cos(t) - 2 * math.cos(2 * t))
        z = 3 if math.sin(3 * t) >= 0 else 1
        points.append((x, y, z))
    points.append((416, 76, 1))
    return points


ROPE = knot_rope()
TRAY_ROPES = [ROPE]



def catmull_rom(pts, samples=48, alpha=0.5):
    """centripetal Catmull-Rom。区間ごとの点列を返す。"""
    P = [pts[0]] + pts + [pts[-1]]
    P = [(P[0][0] * 2 - P[2][0], P[0][1] * 2 - P[2][1])] + P[1:-1] + [
        (P[-1][0] * 2 - P[-3][0], P[-1][1] * 2 - P[-3][1])]
    segs = []
    for i in range(1, len(P) - 2):
        p0, p1, p2, p3 = P[i - 1], P[i], P[i + 1], P[i + 2]

        def tj(ti, a, b):
            return ti + max(math.dist(a, b), 1e-6) ** alpha

        t0 = 0.0
        t1 = tj(t0, p0, p1)
        t2 = tj(t1, p1, p2)
        t3 = tj(t2, p2, p3)
        seg = []
        for k in range(samples + 1):
            t = t1 + (t2 - t1) * k / samples

            def lerp(a, b, ta, tb):
                if tb - ta == 0:
                    return a
                return ((tb - t) / (tb - ta) * a[0] + (t - ta) / (tb - ta) * b[0],
                        (tb - t) / (tb - ta) * a[1] + (t - ta) / (tb - ta) * b[1])
            A1 = lerp(p0, p1, t0, t1)
            A2 = lerp(p1, p2, t1, t2)
            A3 = lerp(p2, p3, t2, t3)
            B1 = lerp(A1, A2, t0, t2)
            B2 = lerp(A2, A3, t1, t3)
            seg.append(lerp(B1, B2, t1, t2))
        segs.append(seg)
    return segs


def pieces(rope):
    """同じ z が続く区間をまとめて (z, 点列, 始端が自由端, 終端が自由端) を返す。"""
    pts = [(x, y) for x, y, _ in rope]
    segs = catmull_rom(pts)
    out = []
    cur_z, cur = None, []
    start_free = True
    for i, seg in enumerate(segs):
        z = rope[i][2]
        if z != cur_z and cur:
            out.append([cur_z, cur, start_free, False])
            start_free = False
            cur = cur[-3:]
        cur_z = z
        cur = cur + seg[1:] if cur else list(seg)
    out.append([cur_z, cur, start_free, True])
    return out


def _seg_dist(X, Y, a, b, butt_a, butt_b):
    ax, ay = a; bx, by = b
    dx, dy = bx - ax, by - ay
    L2 = dx * dx + dy * dy or 1e-9
    t = ((X - ax) * dx + (Y - ay) * dy) / L2
    d = np.hypot(X - (ax + np.clip(t, 0, 1) * dx), Y - (ay + np.clip(t, 0, 1) * dy))
    if butt_a:
        d = np.where(t < 0, np.inf, d)
    if butt_b:
        d = np.where(t > 1, np.inf, d)
    return d


def _piece_dist(pts, sfree, efree, S, k, margin):
    D = np.full((S, S), np.inf)
    n = len(pts) - 1
    for i in range(n):
        a = (pts[i][0] * k, pts[i][1] * k)
        b = (pts[i + 1][0] * k, pts[i + 1][1] * k)
        x0 = max(int(min(a[0], b[0]) - margin), 0); x1 = min(int(max(a[0], b[0]) + margin) + 1, S)
        y0 = max(int(min(a[1], b[1]) - margin), 0); y1 = min(int(max(a[1], b[1]) + margin) + 1, S)
        if x0 >= x1 or y0 >= y1:
            continue
        Y, X = np.mgrid[y0:y1, x0:x1] + 0.5
        d = _seg_dist(X, Y, a, b, i == 0 and not sfree, i == n - 1 and not efree)
        D[y0:y1, x0:x1] = np.minimum(D[y0:y1, x0:x1], d)
    # 接続端は端面の平面で切り落とす（丸くしない）
    Y, X = np.mgrid[0:S, 0:S] + 0.5
    for free, p, q in ((sfree, pts[0], pts[3]), (efree, pts[-1], pts[-4])):
        if not free:
            tx, ty = p[0] - q[0], p[1] - q[1]
            px, py = p[0] * k, p[1] * k
            near = np.hypot(X - px, Y - py) < margin * 1.6
            D = np.where(near & ((X - px) * tx + (Y - py) * ty > 0), np.inf, D)
    return D


def all_pieces(ropes):
    return [p for r in ropes for p in pieces(r)]


def fit(ropes, width, margin=14):
    """ロープ全体の外形が設計座標の中央に収まるよう拡大・平行移動する。"""
    ps = all_pieces(ropes)
    xs = [x for p in ps for x, _ in p[1]]
    ys = [y for p in ps for _, y in p[1]]
    r = width / 2
    x0, x1, y0, y1 = min(xs) - r, max(xs) + r, min(ys) - r, max(ys) + r
    s = (DESIGN - 2 * margin) / max(x1 - x0, y1 - y0)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    return [[((x - cx) * s + DESIGN / 2, (y - cy) * s + DESIGN / 2, z) for x, y, z in r] for r in ropes], width * s, s


def render(ropes, size, width, outline=None, gap=0, ss=4, color=(0, 0, 0), fill_color=None):
    """ロープを描画（width, outline, gap は設計座標）。
    outline 指定時: 縁取りロープ。fill_color で内側の塗り色を指定する。
    outline=None : 太い単線。上を通る線の両脇に gap の隙間を空けて交差を表す。"""
    S = size * ss
    k = S / DESIGN
    R = width / 2 * k
    G = gap * k
    Ri = (width / 2 - outline) * k if outline else None
    alpha = np.zeros((S, S))
    coverage = np.zeros((S, S)) if fill_color is not None else None
    for z, pts, sfree, efree in sorted(all_pieces(ropes), key=lambda p: p[0]):
        D = _piece_dist(pts, sfree, efree, S, k, R + G + 2)
        full = np.clip(R - D + 0.5, 0, 1)
        if coverage is not None:
            coverage = np.maximum(coverage, full)
        if Ri is not None:
            alpha = alpha * (1 - full) + (full - np.clip(Ri - D + 0.5, 0, 1))
        else:
            clear = np.clip(R + G - D + 0.5, 0, 1)
            alpha = np.maximum(alpha * (1 - clear), full)
    a = Image.fromarray((np.clip(alpha, 0, 1) * 255).astype(np.uint8), "L")
    a = a.resize((size, size), Image.BOX)
    img = Image.new("RGBA", (size, size), color + (0,))
    img.putalpha(a)
    if coverage is not None:
        background = Image.new("RGBA", (size, size), fill_color + (0,))
        mask = Image.fromarray((np.clip(coverage, 0, 1) * 255).astype(np.uint8), "L")
        background.putalpha(mask.resize((size, size), Image.BOX))
        img = Image.alpha_composite(background, img)
    return img


def svg(ropes, width, outline=None, gap=0, color="#000", fill_color=None):
    """同じ重なり方を SVG mask で再現したベクター版。"""
    ps = sorted(all_pieces(ropes), key=lambda p: p[0])
    defs, body = [], ""

    def d(pts):
        return "M" + " L".join(f"{x:.1f},{y:.1f}" for x, y in pts)

    def caps(p, r, fill):
        s = ""
        for flag, q in ((p[2], p[1][0]), (p[3], p[1][-1])):
            if flag:
                s += f'<circle cx="{q[0]:.1f}" cy="{q[1]:.1f}" r="{r:.1f}" fill="{fill}"/>'
        return s

    wi = width - 2 * outline if outline else None
    wc = width + 2 * gap if not outline else width
    def extend(p, e=4.0):
        """継ぎ目側の端を接線方向へ e だけ延ばした点列（内側マスクの継ぎ目の筋を防ぐ）。"""
        pts = list(p[1])
        if not p[2]:
            (x0, y0), (x1, y1) = pts[0], pts[1]
            L = math.dist(pts[0], pts[1]) or 1
            pts.insert(0, (x0 - (x1 - x0) / L * e, y0 - (y1 - y0) / L * e))
        if not p[3]:
            (x0, y0), (x1, y1) = pts[-1], pts[-2]
            L = math.dist(pts[-1], pts[-2]) or 1
            pts.append((x0 - (x1 - x0) / L * e, y0 - (y1 - y0) / L * e))
        return d(pts)

    for i, p in enumerate(ps):
        path = d(p[1])
        defs.append(
            f'<mask id="cut{i}" maskUnits="userSpaceOnUse" x="0" y="0" width="{DESIGN}" height="{DESIGN}">'
            f'<rect width="{DESIGN}" height="{DESIGN}" fill="#fff"/>'
            f'<path d="{path}" fill="none" stroke="#000" stroke-width="{wc:.1f}" stroke-linejoin="round"/>'
            f'{caps(p, wc / 2, "#000")}</mask>')
        if wi is None:
            ring = (f'<path d="{path}" fill="none" stroke="{color}" stroke-width="{width:.1f}" '
                    f'stroke-linejoin="round"/>{caps(p, width / 2, color)}')
            body = (f'<g mask="url(#cut{i})">{body}</g>' if body else "") + ring
            continue
        defs.append(
            f'<mask id="in{i}" maskUnits="userSpaceOnUse" x="0" y="0" width="{DESIGN}" height="{DESIGN}">'
            f'<rect width="{DESIGN}" height="{DESIGN}" fill="#fff"/>'
            f'<path d="{extend(p)}" fill="none" stroke="#000" stroke-width="{wi:.1f}" stroke-linejoin="round"/>'
            f'{caps(p, wi / 2, "#000")}</mask>')
        ring = (f'<g mask="url(#in{i})"><path d="{path}" fill="none" stroke="{color}" '
                f'stroke-width="{width}" stroke-linejoin="round"/>{caps(p, width / 2, color)}</g>')
        if fill_color is not None:
            ring = (f'<path d="{path}" fill="none" stroke="{fill_color}" stroke-width="{width}" '
                    f'stroke-linejoin="round"/>{caps(p, width / 2, fill_color)}') + ring
        body = (f'<g mask="url(#cut{i})">{body}</g>' if body else "") + ring
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {DESIGN} {DESIGN}" '
            f'width="{DESIGN}" height="{DESIGN}"><defs>{"".join(defs)}</defs>{body}</svg>\n')


def preview(images, bg, path):
    pad = 24
    w = sum(im.width for im in images) + pad * (len(images) + 1)
    h = max(im.height for im in images) + pad * 2
    canvas = Image.new("RGBA", (w, h), bg)
    x = pad
    for im in images:
        canvas.alpha_composite(im, (x, pad + (h - 2 * pad - im.height)))
        x += im.width + pad
    canvas.save(path)


def main():
    os.makedirs(OUT, exist_ok=True)
    # 太いロープを黒い縁と白い内側で描き、背景は透明にする。
    app_rope, w, s = fit([ROPE], 68, margin=24)
    APP = dict(width=w, outline=16 * s, fill_color=(255, 255, 255))
    tray_rope = app_rope
    TRAY = APP

    with open(os.path.join(OUT, "moyai.svg"), "w", encoding="utf-8") as f:
        f.write(svg(app_rope, width=w, outline=16 * s, fill_color="#fff"))
    with open(os.path.join(OUT, "moyai_tray.svg"), "w", encoding="utf-8") as f:
        f.write(svg(tray_rope, width=w, outline=16 * s, fill_color="#fff"))
    with open(os.path.join(OUT, "moyai_tray_white.svg"), "w", encoding="utf-8") as f:
        f.write(svg(tray_rope, width=w, outline=16 * s, fill_color="#fff"))

    app_sizes = [256, 128, 64, 48, 32, 24, 16]
    app = {sz: (render(app_rope, sz, **APP) if sz >= 48 else render(tray_rope, sz, **TRAY))
           for sz in app_sizes}
    for sz, im in app.items():
        im.save(os.path.join(OUT, f"moyai_{sz}.png"))
    app[256].save(os.path.join(OUT, "moyai.ico"), sizes=[(x, x) for x in app_sizes],
                  append_images=[app[x] for x in app_sizes[1:]])

    tray_sizes = [32, 24, 20, 16]
    for name, col in (("tray", (0, 0, 0)), ("tray_white", (0, 0, 0))):
        imgs = {sz: render(tray_rope, sz, color=col, **TRAY) for sz in tray_sizes}
        for sz, im in imgs.items():
            im.save(os.path.join(OUT, f"moyai_{name}_{sz}.png"))
        imgs[32].save(os.path.join(OUT, f"moyai_{name}.ico"), sizes=[(x, x) for x in tray_sizes],
                      append_images=[imgs[x] for x in tray_sizes[1:]])

    big = render(app_rope, 512, ss=2, **APP)
    big.save(os.path.join(OUT, "moyai_512.png"))
    preview([big], (255, 255, 255, 255), os.path.join(OUT, "preview_large.png"))
    small = [app[x].resize((x * 4, x * 4), Image.NEAREST) for x in (48, 32, 24, 16)]
    preview(small, (255, 255, 255, 255), os.path.join(OUT, "preview_small_light.png"))
    wsmall = [render(tray_rope, x, **TRAY).resize((x * 4, x * 4), Image.NEAREST)
              for x in (32, 24, 16)]
    preview(wsmall, (32, 32, 32, 255), os.path.join(OUT, "preview_tray_dark.png"))
    print("done:", OUT)


if __name__ == "__main__":
    sys.exit(main())

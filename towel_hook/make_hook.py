"""Крючок для полотенец, защёлкивающийся на ручку конусного сечения (вариант A, J-образный).
Профиль лежит в плоскости XY, выдавливается вдоль Z (ось ручки). Печать: профилем на стол.
Оси профиля: x - поперёк ручки, y - от основания к вершине конуса (от двери наружу).
"""
import math, sys
import numpy as np
from shapely.geometry import Polygon, box, Point
from shapely.ops import unary_union
from shapely import affinity
import trimesh

# ---- сечение ручки ----
BASE_W, H, R_TOP, R_SIDE = 9.0, 12.0, 3.0, 40.0
# ---- параметры крючка ----
CLEAR = 0.20      # зазор по профилю (PETG)
WALL = 1.8        # толщина упругих лапок / колпачка
TAB_T = 1.2       # толщина зубца под основанием
TAB_OVER = 0.8    # заход зубца под основание
LENGTH = 20.0     # длина клипсы вдоль ручки (Z)
STEM_W = 4.0      # толщина стержня крючка
REACH = 15.0      # свободный вылет крючка (от колпачка до внутренней грани упора)
LIP_T = 4.0       # толщина упора
LIP_H = 8.0       # высота упора над стержнем
FILLET = 3.0

def handle_profile(n=40):
    ox, oy = 0.0, H - R_TOP                   # центр дуги вершины
    bx, by = BASE_W / 2, 0.0                  # угол основания (правый)
    # центр дуги R40 (правая сторона): |C-O| = R_SIDE-R_TOP, |C-B| = R_SIDE, C слева (выпуклая сторона)
    d = R_SIDE - R_TOP
    # пересечение двух окружностей
    dx, dy = bx - ox, by - oy
    L = math.hypot(dx, dy)
    a = (d**2 - R_SIDE**2 + L**2) / (2 * L)
    h = math.sqrt(d**2 - a**2)
    px, py = ox + a * dx / L, oy + a * dy / L
    cands = [(px + h * dy / L, py - h * dx / L), (px - h * dy / L, py + h * dx / L)]
    cx, cy = min(cands, key=lambda c: c[0])   # центр слева -> сторона выпуклая
    # точка касания
    ux, uy = (ox - cx) / d, (oy - cy) / d
    tx, ty = cx + R_SIDE * ux, cy + R_SIDE * uy
    a0 = math.atan2(by - cy, bx - cx); a1 = math.atan2(ty - cy, tx - cx)
    right = [(cx + R_SIDE * math.cos(a0 + (a1 - a0) * i / n), cy + R_SIDE * math.sin(a0 + (a1 - a0) * i / n)) for i in range(n + 1)]
    b0 = math.atan2(ty - oy, tx - ox); b1 = math.atan2(ty - oy, -tx - ox)
    top = [(ox + R_TOP * math.cos(b0 + (b1 - b0) * i / n), oy + R_TOP * math.sin(b0 + (b1 - b0) * i / n)) for i in range(1, n)]
    left = [(-x, y) for x, y in reversed(right)]
    pts = right + top + left
    info = dict(tangent=(tx, ty), side_angle_base=math.degrees(math.atan2(-(right[1][0]-right[0][0]), right[1][1]-right[0][1])))
    return Polygon(pts), info

def build():
    prof, info = handle_profile()
    xmax = max(x for x, y in prof.exterior.coords)
    assert abs(xmax - BASE_W / 2) < 1e-6, f"профиль шире основания: {xmax}"
    outer = prof.buffer(WALL, join_style=1, resolution=32).intersection(box(-50, -(CLEAR + TAB_T), 50, 50))
    cav = prof.buffer(CLEAR, join_style=1, resolution=32)
    gap = box(-(BASE_W / 2 - TAB_OVER), -10, BASE_W / 2 - TAB_OVER, 0)
    clip = outer.difference(cav).difference(gap)
    # скосы на входе зубцов (нижняя внешняя кромка, чтобы клипса легко садилась)
    ytop = H + WALL
    stem = box(-STEM_W / 2, ytop - 1.0, STEM_W / 2, ytop + REACH + LIP_T)
    # упор: вверх по +x от конца стержня
    lip = box(-STEM_W / 2, ytop + REACH, STEM_W / 2 + LIP_H, ytop + REACH + LIP_T)
    hook = unary_union([stem, lip])
    # скругление внутреннего угла (стержень/упор): добавляем заливку
    f = FILLET
    corner = box(STEM_W / 2, ytop + REACH - f, STEM_W / 2 + f, ytop + REACH)
    from shapely.geometry import Point
    cyl = Point(STEM_W / 2 + f, ytop + REACH - f).buffer(f, resolution=32)
    hook = unary_union([hook, corner.difference(cyl)])
    # округлить внешние углы упора
    hook = hook.buffer(-0.6, join_style=1).buffer(0.6, join_style=1, resolution=16)
    body = unary_union([clip, hook])
    return body, prof, info

def main(out):
    body, prof, info = build()
    mesh = trimesh.creation.extrude_polygon(body, LENGTH)
    mesh.export(out)
    print(info)
    print("watertight:", mesh.is_watertight, "volume mm3: %.0f" % mesh.volume, "bbox:", mesh.bounds.round(2).tolist())
    # предпросмотр
    import matplotlib; matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig, ax = plt.subplots(figsize=(6, 7))
    for g, c, a in ((body, "#3b7", .8), (prof, "#c33", .9)):
        geoms = g.geoms if hasattr(g, "geoms") else [g]
        for p in geoms:
            ax.fill(*p.exterior.xy, color=c, alpha=a)
            for i in p.interiors: ax.fill(*i.xy, color="white")
    ax.set_aspect("equal"); ax.grid(alpha=.3); ax.set_title("Сечение: зелёное - крючок, красное - ручка (мм)")
    fig.savefig(out.replace(".stl", "_profile.png"), dpi=110)

if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "towel_hook.stl")

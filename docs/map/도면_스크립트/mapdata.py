"""v1.2 map data parsed from the verified 3D viewer (docs/map/마르코_수영장맵_3D.html).
Replicates the viewer's buildZones / buildDoors exactly (1 cell = 1 m, x = column-1)."""
import re, json, sys

import os
SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '마르코_수영장맵_3D.html')

def _load(src=SRC):
    s = open(src, encoding='utf8').read()
    raws = re.findall(r'const RAW[12] = `(.*?)`;', s, re.S)
    return s, raws

def parse(raw):
    g = {}
    lines = [l for l in raw.split('\n') if l.strip()]
    for i, line in enumerate(lines):
        m = re.match(r'^\s*(\d+)\s\s', line)
        y = int(m.group(1)) if m else (62 if i == 0 else -1)
        c = line[5:]
        for k, ch in enumerate(c):
            x = k - 1
            if ch != ' ' and -1 <= x <= 64 and -1 <= y <= 62:
                g[(x, y)] = ch
    return g

BAR = set('#+:,%') | {''}
N4 = [(1, 0), (-1, 0), (0, 1), (0, -1)]

SEEDS = [
    {"메인 풀 홀": (20, 45), "로비": (20, 4), "탈의실 L(남)": (18, 15), "탈의실 R(여)": (38, 15),
     "샤워실 L(남)": (14, 27), "샤워실 R(여)": (40, 27), "구조 · 응급실": (4, 16), "직원 통로": (47, 20),
     "기계실": (57, 40), "약품 저장실": (52, 22), "직원 계단실": (52, 13), "하역장": (56, 58),
     "관리 사무실": (52, 4), "직원 구역": (61, 4)},
    {"2층 로비": (28, 20), "관람석": (28, 30), "물탱크실": (5, 15), "관리 복도": (52, 17)},
]
CAT = {"메인 풀 홀": "hall", "로비": "guest", "탈의실 L(남)": "guest", "탈의실 R(여)": "guest",
       "샤워실 L(남)": "shower", "샤워실 R(여)": "shower", "구조 · 응급실": "aid", "직원 통로": "staff",
       "기계실": "plant", "약품 저장실": "plant", "직원 계단실": "staff", "하역장": "staff",
       "관리 사무실": "staff", "직원 구역": "staff", "2층 로비": "spect", "관람석": "spect",
       "물탱크실": "plant", "관리 복도": "staff"}
EXACT = {frozenset(["메인 풀 홀", "구조 · 응급실"]): 2.0, frozenset(["메인 풀 홀", "샤워실 L(남)"]): 4.0,
         frozenset(["메인 풀 홀", "샤워실 R(여)"]): 4.0, frozenset(["메인 풀 홀", "직원 통로"]): 2.0,
         frozenset(["메인 풀 홀", "하역장"]): 4.0}

def grade(w):
    return ("병목", "#d62728") if w < 2.4 else (("경계", "#f08c00") if w < 3.1 else ("개방", "#2f9e44"))

def zones(g, f):
    zid, regs = {}, []
    for y in range(-1, 63):
        for x in range(-1, 65):
            if g.get((x, y), '') in BAR or (x, y) in zid:
                continue
            i = len(regs); cells = []; st = [(x, y)]; zid[(x, y)] = i
            while st:
                cx, cy = st.pop(); cells.append((cx, cy))
                for dx, dy in N4:
                    n = (cx + dx, cy + dy)
                    if g.get(n, '') in BAR or n in zid:
                        continue
                    zid[n] = i; st.append(n)
            regs.append(cells)
    names = [None] * len(regs)
    for nm, p in SEEDS[f].items():
        if p in zid:
            names[zid[p]] = nm
    out = []
    for i, cells in enumerate(regs):
        nm = names[i] or f"구역 {i}"
        objs = {}
        for c in cells:
            ch = g.get(c, '')
            if ch in "ABCDEXRSbc@":
                objs[ch] = objs.get(ch, 0) + 1
        out.append(dict(id=i, name=nm, cells=cells, area=len(cells), cat=CAT.get(nm, 'staff'),
                        cx=sum(c[0] for c in cells) / len(cells) + .5,
                        cy=sum(c[1] for c in cells) / len(cells) + .5, objs=objs))
    return zid, out

def doors(g, zid, Z):
    seen, res = set(), []
    for y in range(-1, 63):
        for x in range(-1, 65):
            if g.get((x, y)) != '+' or (x, y) in seen:
                continue
            run, st = [], [(x, y)]; seen.add((x, y))
            while st:
                c = st.pop(); run.append(c)
                for dx, dy in N4:
                    n = (c[0] + dx, c[1] + dy)
                    if g.get(n) == '+' and n not in seen:
                        seen.add(n); st.append(n)
            adj, outside = set(), False
            for cx, cy in run:
                for dx, dy in N4:
                    n = (cx + dx, cy + dy)
                    if g.get(n, '') == '':
                        outside = True
                    elif n in zid:
                        adj.add(zid[n])
            zs = sorted(adj); w = len(run); exact = False
            if len(zs) == 2:
                k = frozenset([Z[zs[0]]['name'], Z[zs[1]]['name']])
                if k in EXACT:
                    w = EXACT[k]; exact = True
            res.append(dict(cells=run, zones=[Z[z]['name'] for z in zs], outside=outside, width=w, exact=exact))
    return res

def load(src=SRC):
    _, raws = _load(src)
    G = [parse(r) for r in raws]
    data = []
    for f in (0, 1):
        zid, Z = zones(G[f], f)
        D = doors(G[f], zid, Z)
        data.append(dict(grid=G[f], zid=zid, zones=Z, doors=D))
    return data

if __name__ == '__main__':
    data = load()
    for f, d in enumerate(data):
        print(f'== {f+1}층  zones={len(d["zones"])}')
        for z in d['zones']:
            print(f"  {z['name']:<10} {z['area']:>5}  {z['cat']:<6} {z['objs']}")
        for dr in d['doors']:
            xs = [c[0] for c in dr['cells']]; ys = [c[1] for c in dr['cells']]
            print(f"  door {dr['zones']} out={dr['outside']} w={dr['width']} exact={dr['exact']} x{min(xs)}-{max(xs)} y{min(ys)}-{max(ys)}")

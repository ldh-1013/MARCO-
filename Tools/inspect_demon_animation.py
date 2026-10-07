"""Read FBX take lengths and repeated pose boundaries without altering the asset."""
import bisect
import math
import struct
import zlib
from pathlib import Path

data = Path("Assets/Demon Horror Creature with Weapon/Meshes/Demon.fbx").read_bytes()
version = struct.unpack_from("<I", data, 23)[0]
header = "<QQQB" if version >= 7500 else "<IIIB"
header_size = struct.calcsize(header)
ticks_per_second = 46186158000


def properties(pos, count):
    result = []
    formats = {"Y": "h", "C": "?", "I": "i", "F": "f", "D": "d", "L": "q"}
    for _ in range(count):
        kind = chr(data[pos])
        pos += 1
        if kind in formats:
            fmt = "<" + formats[kind]
            result.append(struct.unpack_from(fmt, data, pos)[0])
            pos += struct.calcsize(fmt)
        elif kind in ("S", "R"):
            size = struct.unpack_from("<I", data, pos)[0]
            pos += 4
            raw = data[pos:pos + size]
            result.append(raw.decode("utf-8", "replace") if kind == "S" else raw)
            pos += size
        else:
            count, compressed, size = struct.unpack_from("<III", data, pos)
            pos += 12
            raw = data[pos:pos + size]
            if compressed:
                raw = zlib.decompress(raw)
            result.append(struct.unpack("<" + str(count) + {"f": "f", "d": "d", "l": "q", "i": "i", "b": "?", "c": "B"}[kind], raw))
            pos += size
    return result


def nodes(start, end):
    pos = start
    while pos + header_size <= end:
        stop, count, size, length = struct.unpack_from(header, data, pos)
        if not stop:
            break
        name = data[pos + header_size:pos + header_size + length].decode()
        prop_start = pos + header_size + length
        yield name, properties(prop_start, count), prop_start + size, stop
        pos = stop


objects = {}
links = []
for name, props, start, end in nodes(27, len(data)):
    if name == "Objects":
        for kind, values, child_start, child_end in nodes(start, end):
            if kind in ("AnimationStack", "AnimationLayer", "AnimationCurveNode", "AnimationCurve", "Model"):
                objects[values[0]] = (kind, values, list(nodes(child_start, child_end)))
    elif name == "Connections":
        links = [p for n, p, _, _ in nodes(start, end) if n == "C"]

parents = {}
for connection in links:
    parents.setdefault(connection[1], []).append(connection[2])


def belongs(child, stack, seen=None):
    if child == stack:
        return True
    seen = set() if seen is None else seen
    if child in seen:
        return False
    seen.add(child)
    return any(belongs(parent, stack, seen) for parent in parents.get(child, []))


def sample(times, values, time):
    index = bisect.bisect_right(times, time)
    if index == 0:
        return values[0]
    if index >= len(times):
        return values[-1]
    fraction = (time - times[index - 1]) / (times[index] - times[index - 1])
    return values[index - 1] + fraction * (values[index] - values[index - 1])


for stack, (kind, values, children) in objects.items():
    if kind != "AnimationStack" or not any(key in values[1] for key in ("Punch1", "Idle1", "Walk1")):
        continue
    curves = []
    for identifier, (curve_kind, _, curve_children) in objects.items():
        if curve_kind != "AnimationCurve" or not belongs(identifier, stack):
            continue
        fields = {n: p for n, p, _, _ in curve_children}
        times = [tick / ticks_per_second for tick in fields["KeyTime"][0]]
        vals = fields["KeyValueFloat"][0]
        span = max(vals) - min(vals)
        if span > 0.001:
            curves.append((times, vals, span))
    duration = max(times[-1] for times, _, _ in curves)
    print(values[1].split("\x00")[0], "duration", round(duration, 3), "moving curves", len(curves))
    distances = []
    for frame in range(round(duration * 25) + 1):
        time = frame / 25
        difference = sum(((sample(t, v, time) - v[0]) / span) ** 2 for t, v, span in curves)
        distances.append(math.sqrt(difference / len(curves)))
    minima = [(round(i / 25, 2), round(distances[i], 4)) for i in range(1, len(distances) - 1)
              if distances[i] <= distances[i - 1] and distances[i] < distances[i + 1]]
    print("local pose-return minima (seconds, normalized difference):", minima)

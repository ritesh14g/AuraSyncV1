#!/usr/bin/env python3
"""Turn a raw .glb into a phone-ready one and add it to the AuraSync catalogue.

    python optimize-model.py Turbine.glb --name "Turbine" --desc "Steam Turbine"
    python optimize-model.py Turbine_mobile.glb --preview    # V3 Phase 13: the low-detail variant

The original file is never modified. The output is a sibling `<name>_mobile.glb`, and the
entry is written into models.json ready to upload to Hugging Face.

Why this exists at all: optimising a model is a pure function - same input, same output,
every time - so it belongs here, on a laptop, once per model, rather than on every user's
phone on every first load. See the notes in ModelCatalog.cs for the wider picture.

The two things that actually matter on a phone:

  * GPU memory. A texture costs width x height x 4 bytes of VRAM once decoded, whatever
    its file size. Eighteen 4096x4096 textures is 1.5 GB and the app is killed. This is
    almost always the real problem, and the fix is resolution, not file format.
  * Triangle count. Beyond a few hundred thousand you are paying for detail no phone
    screen can resolve.

So the script measures both, then picks settings per file instead of applying one recipe
to everything. A photogrammetry scan is usually triangle-heavy; a CAD export with PBR
material sets is usually texture-heavy. They need opposite treatment.

Requires: Pillow, and Node (gltfpack is fetched through npx on first run).
"""

import argparse
import io
import json
import os
import shutil
import struct
import subprocess
import sys
import time

try:
    from PIL import Image
except ImportError:
    sys.exit("Pillow is missing. Install it with:  pip install Pillow")


# ---------------------------------------------------------------- budgets

# Rough ceilings for a mid-range Android phone that is also running ARCore. These are
# deliberately conservative; loosen them if your target devices are better.
VRAM_BUDGET_MB = 150      # total decoded texture memory for one model
TRIANGLE_BUDGET = 400_000
MIN_TEXTURE_PX = 512      # never go below this, it starts to look bad
MAX_TEXTURE_PX = 2048
MIN_SIMPLIFY_RATIO = 0.10  # never throw away more than 90% of the triangles

# A decoded texture costs w*h*4 bytes, plus about a third again for the mipmap chain.
VRAM_BYTES_PER_PIXEL = 4 * 4 / 3


# ---------------------------------------------------------------- glb io

def read_glb(path):
    with open(path, "rb") as fh:
        magic, version, total = struct.unpack("<4sII", fh.read(12))
        if magic != b"glTF":
            sys.exit(f"{path} is not a GLB file (magic was {magic!r})")
        if version != 2:
            sys.exit(f"{path} is glTF version {version}; this script handles version 2")

        gltf, binary = None, b""
        while fh.tell() < total:
            head = fh.read(8)
            if len(head) < 8:
                break
            clen, ctype = struct.unpack("<I4s", head)
            data = fh.read(clen)
            if ctype == b"JSON":
                gltf = json.loads(data)
            elif ctype == b"BIN\x00":
                binary = data
    if gltf is None:
        sys.exit(f"{path} has no JSON chunk")
    return gltf, binary


def write_glb(path, gltf, binary):
    js = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    js += b" " * ((4 - len(js) % 4) % 4)          # JSON pads with spaces
    binary = binary + b"\x00" * ((4 - len(binary) % 4) % 4)   # BIN pads with zeros

    total = 12 + 8 + len(js) + 8 + len(binary)
    with open(path, "wb") as fh:
        fh.write(struct.pack("<4sII", b"glTF", 2, total))
        fh.write(struct.pack("<I4s", len(js), b"JSON"))
        fh.write(js)
        fh.write(struct.pack("<I4s", len(binary), b"BIN\x00"))
        fh.write(binary)
    return total


# ---------------------------------------------------------------- analysis

def image_pixel_size(blob, mime):
    """Dimensions without decoding the whole image."""
    try:
        with Image.open(io.BytesIO(blob)) as im:
            return im.size
    except Exception:
        return (0, 0)


def analyse(gltf, binary):
    views = gltf.get("bufferViews", [])
    accessors = gltf.get("accessors", [])

    textures = []
    for img in gltf.get("images", []):
        bv = img.get("bufferView")
        if bv is None or bv >= len(views):
            continue
        off = views[bv].get("byteOffset", 0)
        ln = views[bv].get("byteLength", 0)
        w, h = image_pixel_size(binary[off:off + ln], img.get("mimeType", ""))
        textures.append({"bytes": ln, "w": w, "h": h})

    tris = 0
    for mesh in gltf.get("meshes", []):
        for prim in mesh.get("primitives", []):
            idx = prim.get("indices")
            if idx is not None and idx < len(accessors):
                tris += accessors[idx].get("count", 0) // 3

    texture_bytes = sum(t["bytes"] for t in textures)
    vram = sum(t["w"] * t["h"] * VRAM_BYTES_PER_PIXEL for t in textures)

    # The Parts drawer walks the Unity transform hierarchy, and glTFast makes one
    # GameObject per glTF node. So the number of nodes carrying a mesh is what decides
    # how many rows the drawer shows - and what the exploded view has to work with.
    nodes = gltf.get("nodes", [])

    return {
        "textures": textures,
        "texture_count": len(textures),
        "texture_bytes": texture_bytes,
        "geometry_bytes": max(0, len(binary) - texture_bytes),
        "vram_bytes": vram,
        "triangles": tris,
        "max_texture_px": max([max(t["w"], t["h"]) for t in textures], default=0),
        "nodes": len(nodes),
        "mesh_nodes": sum(1 for n in nodes if n.get("mesh") is not None),
        "meshes": len(gltf.get("meshes", [])),
    }


def choose_settings(info):
    """Pick a texture cap and a simplify ratio from what the model actually contains."""
    n = info["texture_count"]

    # Split the VRAM budget across however many textures there are, then work back to a
    # square edge length and round down to a power of two.
    if n:
        per_texture = (VRAM_BUDGET_MB * 1024 * 1024) / n
        edge = int((per_texture / VRAM_BYTES_PER_PIXEL) ** 0.5)
        cap = 1 << max(0, edge.bit_length() - 1)          # round down to power of 2
        cap = max(MIN_TEXTURE_PX, min(MAX_TEXTURE_PX, cap))
        cap = min(cap, info["max_texture_px"] or cap)     # never upscale
    else:
        cap = 0

    if info["triangles"] > TRIANGLE_BUDGET:
        ratio = max(MIN_SIMPLIFY_RATIO, TRIANGLE_BUDGET / info["triangles"])
    else:
        ratio = 1.0

    return cap, ratio


# ---------------------------------------------------------------- texture pass

def non_colour_images(gltf):
    """Images used as data (normal / roughness / occlusion) rather than as a picture.

    These must not become JPEG: they store a number per channel, and JPEG's ringing
    around edges turns into visible lighting artifacts.
    """
    out = set()
    textures = gltf.get("textures", [])

    def mark(texinfo):
        if not texinfo:
            return
        t = texinfo.get("index")
        if t is not None and t < len(textures):
            src = textures[t].get("source")
            if src is not None:
                out.add(src)

    for mat in gltf.get("materials", []):
        mark(mat.get("normalTexture"))
        mark(mat.get("occlusionTexture"))
        mark(mat.get("pbrMetallicRoughness", {}).get("metallicRoughnessTexture"))
    return out


def shrink_textures(gltf, binary, cap, verbose=True):
    views = gltf.get("bufferViews", [])
    data_maps = non_colour_images(gltf)
    replacement = {}

    for n, img in enumerate(gltf.get("images", [])):
        bv = img.get("bufferView")
        if bv is None or bv >= len(views):
            continue
        off = views[bv].get("byteOffset", 0)
        ln = views[bv].get("byteLength", 0)
        blob = binary[off:off + ln]
        if not blob:
            continue

        try:
            im = Image.open(io.BytesIO(blob))
            im.load()
        except Exception as exc:
            print(f"    image {n}: could not read it ({exc}) - leaving it alone")
            continue

        w, h = im.size
        if max(w, h) > cap:
            scale = cap / max(w, h)
            im = im.resize((max(1, round(w * scale)), max(1, round(h * scale))),
                           Image.LANCZOS)

        has_alpha = im.mode in ("RGBA", "LA") or "transparency" in im.info
        if im.mode not in ("RGB", "RGBA", "L"):
            im = im.convert("RGBA" if has_alpha else "RGB")

        buf = io.BytesIO()
        if n in data_maps or has_alpha:
            im.save(buf, format="PNG", optimize=True)
            mime = "image/png"
        else:
            im.save(buf, format="JPEG", quality=90, optimize=True)
            mime = "image/jpeg"

        new = buf.getvalue()
        if verbose:
            kind = "data-map" if n in data_maps else "colour"
            print(f"    image {n:<3} {kind:<9} {w}x{h} -> {im.size[0]}x{im.size[1]}"
                  f"   {mb(ln)} -> {mb(len(new))}")
        replacement[bv] = new
        img["mimeType"] = mime

    if not replacement:
        return binary

    # Rebuild the binary chunk: changing image sizes shifts every later offset, so each
    # bufferView gets a fresh 4-byte-aligned position. Accessors reference views by
    # index, not offset, so they stay valid.
    out = bytearray()
    for i, view in enumerate(views):
        if i in replacement:
            chunk = replacement[i]
        else:
            o = view.get("byteOffset", 0)
            chunk = binary[o:o + view.get("byteLength", 0)]
        while len(out) % 4:
            out.append(0)
        view["byteOffset"] = len(out)
        view["byteLength"] = len(chunk)
        out.extend(chunk)

    if gltf.get("buffers"):
        gltf["buffers"][0]["byteLength"] = len(out)
    return bytes(out)


# ---------------------------------------------------------------- geometry pass

def ensure_node_names(gltf):
    """Give every mesh-bearing node a name, so gltfpack's -kn will protect it.

    gltfpack merges meshes by default to cut draw calls, and -kn exempts only *named*
    nodes. An unnamed node is therefore fair game and gets folded into its neighbours -
    which collapses the model to a single part and empties the Parts drawer. Naming the
    nodes first makes -kn cover all of them. Existing names are never overwritten.
    """
    nodes = gltf.get("nodes", [])
    meshes = gltf.get("meshes", [])
    named = 0
    for i, node in enumerate(nodes):
        if node.get("mesh") is None or node.get("name"):
            continue
        mesh = meshes[node["mesh"]] if node["mesh"] < len(meshes) else {}
        node["name"] = mesh.get("name") or f"Part_{i + 1:02d}"
        named += 1
    return named


def collapse_kn_wrappers(gltf):
    """Fold gltfpack's -kn wrapper nodes back into the parts they wrap.

    -kn preserves a named node by turning it into a transform anchor and moving its mesh
    down onto a fresh unnamed child. The model still has all its parts, but the Parts
    drawer then lists each one twice - once as "Object_7", once as "(unnamed)" - because
    it lists every node that contains something visible.

    Where the named parent has no transform of its own, pulling the child's mesh and
    transform back up is exactly equivalent and restores the original shape of the tree.
    """
    nodes = gltf.get("nodes", [])
    if not nodes:
        return 0

    # Animations and skins address nodes by index; re-indexing around them is not worth
    # the risk for the small cosmetic gain, so leave those models alone.
    if gltf.get("animations") or gltf.get("skins"):
        return 0

    TRANSFORM_KEYS = ("matrix", "translation", "rotation", "scale")
    merged = set()

    for node in nodes:
        kids = node.get("children") or []
        if not node.get("name") or len(kids) != 1:
            continue
        if any(k in node for k in TRANSFORM_KEYS) or "mesh" in node:
            continue

        child = nodes[kids[0]]
        if child.get("name") or child.get("children") or "mesh" not in child:
            continue

        node["mesh"] = child["mesh"]
        for key in TRANSFORM_KEYS:
            if key in child:
                node[key] = child[key]
        for key in ("camera", "extensions", "extras"):
            if key in child and key not in node:
                node[key] = child[key]
        del node["children"]
        merged.add(kids[0])

    if not merged:
        return 0

    # Removing nodes shifts every later index, so build an old -> new map and rewrite
    # every reference to a node: children lists and the scene roots.
    remap, kept = {}, []
    for i, node in enumerate(nodes):
        if i in merged:
            continue
        remap[i] = len(kept)
        kept.append(node)

    for node in kept:
        if "children" in node:
            node["children"] = [remap[c] for c in node["children"] if c in remap]
            if not node["children"]:
                del node["children"]

    for scene in gltf.get("scenes", []):
        if "nodes" in scene:
            scene["nodes"] = [remap[n] for n in scene["nodes"] if n in remap]

    gltf["nodes"] = kept
    return len(merged)


# ---------------------------------------------------------------- splitting a part

ACCESSOR_DTYPES = {5120: "i1", 5121: "u1", 5122: "i2", 5123: "u2", 5125: "u4", 5126: "f4"}
ACCESSOR_WIDTH = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}


def read_accessor(gltf, binary, index):
    """An accessor's data as a numpy array of shape (count, width). Uncompressed files only."""
    import numpy as np

    acc = gltf["accessors"][index]
    view = gltf["bufferViews"][acc["bufferView"]]
    dtype = np.dtype(ACCESSOR_DTYPES[acc["componentType"]])
    width = ACCESSOR_WIDTH[acc["type"]]
    start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    stride = view.get("byteStride", 0)
    count = acc["count"]

    if stride and stride != dtype.itemsize * width:
        raw = np.frombuffer(binary, np.uint8, count=stride * (count - 1) + dtype.itemsize * width,
                            offset=start)
        rows = np.lib.stride_tricks.as_strided(raw, (count, dtype.itemsize * width), (stride, 1))
        return rows.copy().view(dtype).reshape(count, width)
    return np.frombuffer(binary, dtype, count=count * width, offset=start).reshape(count, width)


def mesh_islands(positions, indices):
    """Label each triangle with the loose piece it belongs to.

    Vertices are welded by position first: a model file duplicates a vertex wherever its
    texture coordinates or normal change, so two triangles sharing a corner in space may
    share no index at all. Without welding, every UV seam would count as a separate piece.
    """
    import numpy as np

    _, weld = np.unique(np.round(positions, 5), axis=0, return_inverse=True)
    tris = weld.ravel()[indices.reshape(-1, 3)]

    parent = list(range(int(weld.max()) + 1))

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    for a, b, c in tris.tolist():
        ra, rb, rc = find(a), find(b), find(c)
        parent[rb] = ra
        parent[find(rc)] = ra

    return np.array([find(t[0]) for t in tris.tolist()])


def split_part(gltf, binary, node_name, gap_ratio=0.15):
    """Split one mesh node into several parts, by where its loose pieces sit.

    Some models group geometry by *material* rather than by function: a jet engine's fan
    blades and its rear blade disk share a "blades" material, so they arrive as one mesh
    and can only be selected, explained or spun together. This finds the mesh's loose
    pieces, sorts them along the mesh's longest axis, and starts a new part wherever there
    is a gap wider than `gap_ratio` of that length. The fan's 37 blades and hub stay
    together; the disk at the far end becomes its own part.

    The original node is kept, as a group with no mesh of its own, and the new parts become
    its children (`Object_6` -> `Object_6.1`, `Object_6.2`). That matters: a part's identity
    in the app is its position in the hierarchy, so replacing the node would shift every
    later sibling's position and break anything keyed to them. Growing a level below leaves
    every other part exactly where it was.

    Returns the new binary chunk and the number of parts made (1 means nothing to split).
    """
    import numpy as np

    if "EXT_meshopt_compression" in gltf.get("extensionsUsed", []):
        sys.exit("--split needs the original, uncompressed .glb (this one is already compressed)")

    nodes = gltf.get("nodes", [])
    matches = [i for i, n in enumerate(nodes) if n.get("name") == node_name and "mesh" in n]
    if not matches:
        sys.exit(f"--split: no mesh node named '{node_name}'")
    if len(matches) > 1:
        sys.exit(f"--split: {len(matches)} nodes are named '{node_name}'; the name must be unique")
    node_index = matches[0]
    node = nodes[node_index]

    mesh = gltf["meshes"][node["mesh"]]
    if len(mesh["primitives"]) != 1:
        sys.exit(f"--split: '{node_name}' has {len(mesh['primitives'])} primitives; only 1 is supported")
    prim = mesh["primitives"][0]
    if "indices" not in prim or prim.get("mode", 4) != 4:
        sys.exit(f"--split: '{node_name}' is not an indexed triangle mesh")

    positions = read_accessor(gltf, binary, prim["attributes"]["POSITION"]).astype(np.float64)
    indices = read_accessor(gltf, binary, prim["indices"]).ravel().astype(np.int64)
    labels = mesh_islands(positions, indices)

    # Where each loose piece sits along the mesh's longest axis.
    tri_vertices = positions[indices.reshape(-1, 3)]           # (tris, 3 corners, xyz)
    tri_centres = tri_vertices.mean(axis=1)
    extent = positions.max(axis=0) - positions.min(axis=0)
    axis = int(np.argmax(extent))
    pieces = np.unique(labels)
    centre_of = {p: tri_centres[labels == p][:, axis].mean() for p in pieces}

    ordered = sorted(pieces, key=lambda p: centre_of[p])
    groups, current = [], [ordered[0]]
    for prev, piece in zip(ordered, ordered[1:]):
        if centre_of[piece] - centre_of[prev] > gap_ratio * extent[axis]:
            groups.append(current)
            current = []
        current.append(piece)
    groups.append(current)

    print(f"    split '{node_name}': {len(pieces)} loose piece(s) -> {len(groups)} part(s) "
          f"along {'XYZ'[axis]}")
    if len(groups) < 2:
        return binary, 1

    tri_indices = indices.reshape(-1, 3)
    new_children = []
    for k, group in enumerate(groups, start=1):
        mask = np.isin(labels, group)
        part_indices = tri_indices[mask].ravel().astype(np.uint32)

        # Index data goes on the end of the binary chunk, 4-byte aligned.
        binary += b"\x00" * ((4 - len(binary) % 4) % 4)
        gltf["bufferViews"].append({
            "buffer": 0,
            "byteOffset": len(binary),
            "byteLength": part_indices.nbytes,
            "target": 34963,  # ELEMENT_ARRAY_BUFFER
        })
        binary += part_indices.tobytes()
        gltf["accessors"].append({
            "bufferView": len(gltf["bufferViews"]) - 1,
            "componentType": 5125,  # UNSIGNED_INT
            "count": int(part_indices.size),
            "type": "SCALAR",
        })

        # The new mesh shares the original vertex data; only the triangle list differs.
        new_prim = dict(prim)
        new_prim["indices"] = len(gltf["accessors"]) - 1
        gltf["meshes"].append({"name": f"{mesh.get('name', node_name)}.{k}", "primitives": [new_prim]})

        nodes.append({"name": f"{node_name}.{k}", "mesh": len(gltf["meshes"]) - 1})
        new_children.append(len(nodes) - 1)
        print(f"      {node_name}.{k}: {len(group)} piece(s), {int(mask.sum()):,} triangles")

    # The original node keeps its transform and becomes the group the new parts sit in.
    del node["mesh"]
    node["children"] = (node.get("children") or []) + new_children

    gltf["buffers"][0]["byteLength"] = len(binary)
    return binary, len(groups)


def find_npx():
    for name in ("npx", "npx.cmd"):
        if shutil.which(name):
            return shutil.which(name)
    return None


def run_gltfpack(src, dst, ratio):
    npx = find_npx()
    if not npx:
        print("    ! npx not found - skipping geometry compression.")
        print("      Install Node.js, then re-run to get the geometry savings too.")
        shutil.copyfile(src, dst)
        return False

    # -kn keeps named nodes separate instead of merging meshes together. Without it the
    # Parts drawer and the exploded view both collapse to a single part; the file is a
    # little larger with it, which is the right trade for a model you can take apart.
    # -ke keeps any extras data authoring tools attached to the nodes.
    cmd = [npx, "--yes", "gltfpack", "-i", src, "-o", dst, "-cc", "-kn", "-ke"]
    if ratio < 1.0:
        # -slb locks border vertices so connected pieces do not pull apart at the seams.
        cmd += ["-si", f"{ratio:.3f}", "-slb"]

    proc = subprocess.run(cmd, capture_output=True, text=True)
    if proc.returncode != 0 or not os.path.exists(dst):
        print(f"    ! gltfpack failed: {(proc.stderr or proc.stdout).strip()[:300]}")
        shutil.copyfile(src, dst)
        return False
    return True


# ---------------------------------------------------------------- manifest

def update_manifest(path, entry, base_url):
    if os.path.exists(path):
        with open(path, "r", encoding="utf-8") as fh:
            manifest = json.load(fh)
    else:
        manifest = {"baseUrl": base_url, "models": []}

    manifest.setdefault("baseUrl", base_url)
    models = manifest.setdefault("models", [])

    for i, m in enumerate(models):
        if m.get("id") == entry["id"]:
            entry["createdAt"] = m.get("createdAt", entry["createdAt"])
            # Keep fields this script does not own (partsUrl, context, ...): re-optimising a
            # model must not silently drop its part knowledge file from the catalogue.
            merged = dict(m)
            merged.update(entry)
            models[i] = merged
            action = "updated"
            break
    else:
        models.append(entry)
        action = "added"

    with open(path, "w", encoding="utf-8") as fh:
        json.dump(manifest, fh, indent=2)
        fh.write("\n")
    return action


# ---------------------------------------------------------------- reporting

def mb(n):
    return f"{n / 1048576:.2f} MB"


def describe(label, info, size):
    print(f"  {label}")
    print(f"    file            {mb(size)}")
    print(f"    geometry        {mb(info['geometry_bytes'])}   "
          f"{info['triangles']:,} triangles")
    print(f"    textures        {mb(info['texture_bytes'])}   "
          f"{info['texture_count']} image(s), largest {info['max_texture_px']}px")
    print(f"    GPU memory      {mb(info['vram_bytes'])}")
    print(f"    parts           {info['mesh_nodes']} "
          f"(of {info['nodes']} nodes, {info['meshes']} mesh(es))")



# ---------------------------------------------------------------- preview variant (V3 Phase 13)

PREVIEW_TEXTURE_PX = 256
PREVIEW_SIMPLIFY = 0.25


def node_tree(gltf):
    """The part hierarchy as the app sees it: child-index paths and names, from the scene roots."""
    nodes = gltf.get("nodes", [])
    scenes = gltf.get("scenes") or [{}]
    scene = scenes[gltf.get("scene", 0)]
    out = []

    def walk(i, path):
        n = nodes[i]
        # The node's own transform is part of the identity: notes, cones and pings are stored in
        # a part's local space, so a preview whose part moved or rescaled would misplace them.
        xf = tuple(json.dumps(n.get(k), sort_keys=True) for k in ("matrix", "translation", "rotation", "scale"))
        out.append((path, n.get("name", ""), n.get("mesh") is not None, xf))
        for k, c in enumerate(n.get("children", [])):
            walk(c, f"{path}/{k}")

    for k, r in enumerate(scene.get("nodes", [])):
        walk(r, str(k))
    return out


def relayout_binary(gltf, binary, replacement):
    """Rebuilds the BIN chunk with some bufferViews' bytes replaced.

    Unlike shrink_textures' rebuild this understands EXT_meshopt_compression, which the mobile
    files use: a compressed view keeps its real bytes in buffer 0 through the extension, while the
    view itself points at a fallback buffer that has no bytes in the file. Every block that lives
    in buffer 0 is moved; nothing else is touched.
    """
    blocks = []  # (old offset, holder dict, view index or None)
    for i, view in enumerate(gltf.get("bufferViews", [])):
        if view.get("buffer", 0) == 0:
            blocks.append((view.get("byteOffset", 0), view, i))
        ext = view.get("extensions", {}).get("EXT_meshopt_compression")
        if ext and ext.get("buffer", 0) == 0:
            blocks.append((ext.get("byteOffset", 0), ext, None))
    blocks.sort(key=lambda b: b[0])

    old = {id(h): (h.get("byteOffset", 0), h.get("byteLength", 0)) for _, h, _ in blocks}
    out = bytearray()
    for _, holder, vi in blocks:
        if vi is not None and vi in replacement:
            chunk = replacement[vi]
        else:
            o, ln = old[id(holder)]
            chunk = binary[o:o + ln]
        while len(out) % 16:          # meshopt needs 4-byte alignment; 16 is safe for everything
            out.append(0)
        holder["byteOffset"] = len(out)
        holder["byteLength"] = len(chunk)
        out.extend(chunk)

    gltf["buffers"][0]["byteLength"] = len(out)
    return bytes(out)


def preview_textures(gltf, binary, cap):
    """Every image down to cap pixels (JPEG unless it is a data map or has alpha)."""
    views = gltf.get("bufferViews", [])
    data_maps = non_colour_images(gltf)
    replacement = {}
    for n, img in enumerate(gltf.get("images", [])):
        bv = img.get("bufferView")
        if bv is None or bv >= len(views) or views[bv].get("buffer", 0) != 0:
            continue
        off, ln = views[bv].get("byteOffset", 0), views[bv].get("byteLength", 0)
        try:
            im = Image.open(io.BytesIO(binary[off:off + ln]))
            im.load()
        except Exception as exc:
            print(f"    image {n}: could not read it ({exc}) - leaving it alone")
            continue
        w, h = im.size
        if max(w, h) > cap:
            scale = cap / max(w, h)
            im = im.resize((max(1, round(w * scale)), max(1, round(h * scale))), Image.LANCZOS)
        has_alpha = im.mode in ("RGBA", "LA") or "transparency" in im.info
        if im.mode not in ("RGB", "RGBA", "L"):
            im = im.convert("RGBA" if has_alpha else "RGB")
        buf = io.BytesIO()
        if n in data_maps or has_alpha:
            im.save(buf, format="PNG", optimize=True)
            img["mimeType"] = "image/png"
        else:
            im.save(buf, format="JPEG", quality=80, optimize=True)
            img["mimeType"] = "image/jpeg"
        replacement[bv] = buf.getvalue()
    return relayout_binary(gltf, binary, replacement), len(replacement)


def make_preview(src, cap, ratio, manifest_path):
    """<name>_preview.glb from a finished <name>_mobile.glb, with the IDENTICAL part hierarchy.

    The app keys every part by its child-index path, and swaps the preview for the mobile file
    mid-session, so the two must agree node for node. Textures are shrunk in place (the hierarchy
    is not touched at all). Geometry is simplified with gltfpack only if the result still has the
    very same tree; otherwise the texture-only file is kept.
    """
    stem = os.path.splitext(os.path.basename(src))[0]
    base = stem[:-7] if stem.endswith("_mobile") else stem
    out_path = os.path.join(os.path.dirname(os.path.abspath(src)), f"{base}_preview.glb")

    print(f"\n{'=' * 66}\nPREVIEW of {src}\n{'=' * 66}")
    gltf, binary = read_glb(src)
    tree = node_tree(gltf)
    binary, shrunk = preview_textures(gltf, binary, cap)
    print(f"    textures        {shrunk} image(s) capped at {cap}px")
    write_glb(out_path, gltf, binary)

    if ratio < 1.0 and find_npx():
        tmp = out_path[:-4] + ".simplified.glb"
        cmd = [find_npx(), "--yes", "gltfpack", "-i", out_path, "-o", tmp, "-cc", "-kn", "-ke",
               "-si", f"{ratio:.3f}", "-slb"]
        proc = subprocess.run(cmd, capture_output=True, text=True)
        if proc.returncode == 0 and os.path.exists(tmp):
            g2, b2 = read_glb(tmp)
            collapse_kn_wrappers(g2)
            if node_tree(g2) == tree:
                write_glb(out_path, g2, b2)
                print(f"    geometry        simplified to {ratio * 100:.0f}% (part tree unchanged)")
            else:
                print("    geometry        simplifying changed the part tree or a part transform - kept the full geometry")
            os.remove(tmp)
        else:
            print("    geometry        gltfpack failed, kept the full geometry: "
                  f"{(proc.stderr or proc.stdout).strip()[:200]}")

    g3, _ = read_glb(out_path)
    if node_tree(g3) != tree:
        os.remove(out_path)
        sys.exit("  ! The preview's part tree differs from the mobile file's. Nothing written.")
    parts = sum(1 for _, _, m, _ in tree if m)
    size_m, size_p = os.path.getsize(src), os.path.getsize(out_path)
    print(f"    parts           {parts} (identical tree: {len(tree)} nodes)")
    print(f"    size            {mb(size_m)} -> {mb(size_p)} ({size_m / max(1, size_p):.1f}x smaller)")

    # Catalogue: the entry whose glbUrl is this mobile file gets the preview and both sizes.
    if os.path.exists(manifest_path):
        with open(manifest_path, "r", encoding="utf-8") as fh:
            manifest = json.load(fh)
        hit = False
        for m in manifest.get("models", []):
            if m.get("glbUrl") == os.path.basename(src):
                m["previewUrl"] = os.path.basename(out_path)
                m["previewBytes"] = size_p
                m["glbBytes"] = size_m
                hit = True
        if hit:
            with open(manifest_path, "w", encoding="utf-8") as fh:
                json.dump(manifest, fh, indent=2)
                fh.write("\n")
            print(f"    {manifest_path}: previewUrl, previewBytes and glbBytes set")
        else:
            print(f"  ! No entry in {manifest_path} has glbUrl {os.path.basename(src)}; catalogue not changed")
    print(f"\n  Next: upload {os.path.basename(out_path)} and {manifest_path} to Hugging Face.\n")

# ---------------------------------------------------------------- main

def main():
    ap = argparse.ArgumentParser(
        description="Make a raw .glb phone-ready and add it to models.json.")
    ap.add_argument("input", help="the raw .glb (never modified)")
    ap.add_argument("--id", help="catalogue id (default: filename, lowercased)")
    ap.add_argument("--name", help="display name (default: filename)")
    ap.add_argument("--desc", default="", help="subtitle shown on the model card")
    ap.add_argument("--thumb", default="", help="full URL of a preview image")
    ap.add_argument("--manifest", default="models.json", help="catalogue file to update")
    ap.add_argument("--base-url",
                    default="https://huggingface.co/datasets/Ritesh121/ARModels/resolve/main",
                    help="used only when creating a new manifest")
    ap.add_argument("--max-texture", type=int, default=0,
                    help="override the automatic texture cap, in pixels")
    ap.add_argument("--simplify", type=float, default=0.0,
                    help="override the automatic triangle ratio, e.g. 0.5")
    ap.add_argument("--no-geometry", action="store_true",
                    help="textures only; leaves the part hierarchy completely untouched")
    ap.add_argument("--dry-run", action="store_true",
                    help="report the plan and change nothing")
    ap.add_argument("--split", action="append", default=[], metavar="NODE",
                    help="split this mesh node into parts by its loose pieces (repeatable); "
                         "needs the original, uncompressed .glb")
    ap.add_argument("--out", default="",
                    help="output file name (default <name>_mobile.glb). Use a NEW name when the "
                         "parts change: phones cache models by URL and never re-download one "
                         "they already have")
    ap.add_argument("--preview", action="store_true",
                    help="V3 Phase 13: make <name>_preview.glb from a finished <name>_mobile.glb "
                         "(small textures, same part tree) and add it to the catalogue entry")
    ap.add_argument("--preview-texture", type=int, default=PREVIEW_TEXTURE_PX,
                    help=f"preview texture cap in pixels (default {PREVIEW_TEXTURE_PX})")
    ap.add_argument("--preview-simplify", type=float, default=PREVIEW_SIMPLIFY,
                    help=f"preview triangle ratio, used only if the part tree survives "
                         f"(default {PREVIEW_SIMPLIFY}; 1 = off)")
    args = ap.parse_args()

    if args.preview:
        if not os.path.exists(args.input):
            sys.exit(f"No such file: {args.input}")
        make_preview(args.input, args.preview_texture, args.preview_simplify, args.manifest)
        return

    src = args.input
    if not os.path.exists(src):
        sys.exit(f"No such file: {src}")

    stem = os.path.splitext(os.path.basename(src))[0]
    out_name = args.out or f"{stem}_mobile.glb"
    if not out_name.lower().endswith(".glb"):
        out_name += ".glb"
    out_path = os.path.join(os.path.dirname(os.path.abspath(src)), out_name)
    model_id = args.id or stem.lower().replace("_", "-")
    name = args.name or stem

    print(f"\n{'=' * 66}\n{src}\n{'=' * 66}")
    gltf, binary = read_glb(src)
    before = analyse(gltf, binary)
    describe("BEFORE", before, os.path.getsize(src))

    cap, ratio = choose_settings(before)
    if args.max_texture:
        cap = args.max_texture
    if args.simplify:
        ratio = args.simplify

    print("\n  PLAN")
    if before["texture_count"] == 0:
        print("    textures        none to shrink")
    elif cap >= before["max_texture_px"]:
        print(f"    textures        already within budget, leaving at "
              f"{before['max_texture_px']}px")
    else:
        print(f"    textures        cap at {cap}px "
              f"(from {before['max_texture_px']}px, {before['texture_count']} image(s))")
    if args.no_geometry:
        print("    triangles       untouched (--no-geometry)")
        print("    geometry        not compressed (--no-geometry)")
    else:
        if ratio >= 1.0:
            print(f"    triangles       {before['triangles']:,} is within budget, keeping all")
        else:
            print(f"    triangles       simplify to {ratio * 100:.0f}% "
                  f"(~{int(before['triangles'] * ratio):,})")
        print("    geometry        meshopt compression (-cc), parts kept with -kn")

    if args.dry_run:
        print("\n  dry run - nothing written\n")
        return

    started = time.time()
    # gltfpack dispatches on the file extension, so the intermediate has to stay .glb.
    tmp = out_path[:-4] + ".tmp.glb"

    print("\n  WORKING")
    expected_parts = before["mesh_nodes"]
    for node_name in args.split:
        binary, made = split_part(gltf, binary, node_name)
        expected_parts += made - 1

    if cap and cap < before["max_texture_px"]:
        binary = shrink_textures(gltf, binary, cap)
    else:
        print("    (textures untouched)")

    named = ensure_node_names(gltf)
    if named:
        print(f"    named {named} unnamed part(s) so they survive compression")
    write_glb(tmp, gltf, binary)

    if args.no_geometry:
        print("    skipping geometry compression (--no-geometry)")
        shutil.move(tmp, out_path)
    else:
        print("    compressing geometry...")
        run_gltfpack(tmp, out_path, ratio)
        os.remove(tmp)

    gltf2, binary2 = read_glb(out_path)
    folded = collapse_kn_wrappers(gltf2)
    if folded:
        print(f"    folded {folded} wrapper node(s) back into their named parts")
        write_glb(out_path, gltf2, binary2)
        gltf2, binary2 = read_glb(out_path)
    after = analyse(gltf2, binary2)
    size_before, size_after = os.path.getsize(src), os.path.getsize(out_path)

    print()
    describe("AFTER", after, size_after)
    print(f"\n  {size_before / max(1, size_after):.1f}x smaller, "
          f"GPU memory {before['vram_bytes'] / max(1, after['vram_bytes']):.1f}x lower, "
          f"in {time.time() - started:.0f}s")

    if after["vram_bytes"] > VRAM_BUDGET_MB * 1048576 * 1.2:
        print(f"  ! Still above the {VRAM_BUDGET_MB} MB GPU budget. "
              f"Try --max-texture {max(MIN_TEXTURE_PX, cap // 2)}")

    # A collapsed hierarchy empties the Parts drawer and disables the exploded view, and
    # it is invisible in the size figures - so it gets checked rather than assumed.
    if expected_parts > 1 and after["mesh_nodes"] < expected_parts:
        print(f"  ! PARTS LOST: {expected_parts} -> {after['mesh_nodes']}. "
              f"The Parts drawer and exploded view need these.")
        print("    gltfpack merged meshes despite -kn. Re-run with --no-geometry "
              "to keep the hierarchy intact.")
    elif expected_parts > 1:
        print(f"  parts preserved: {after['mesh_nodes']}"
              + (f" ({before['mesh_nodes']} before splitting)" if args.split else ""))

    entry = {
        "id": model_id,
        "name": name,
        "glbUrl": os.path.basename(out_path),
        "thumbnailUrl": args.thumb,
        "description": args.desc,
        "createdAt": int(time.time()),
    }
    action = update_manifest(args.manifest, entry, args.base_url)

    print(f"\n  {action} '{model_id}' in {args.manifest}")
    print(f"\n  Next: upload {os.path.basename(out_path)} to Hugging Face, "
          f"then {args.manifest}.")
    print(f"  Your original {os.path.basename(src)} was not modified.\n")


if __name__ == "__main__":
    main()

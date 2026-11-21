#!/usr/bin/env python3
"""Writes the benchmark graphs. Run once; the JSON it writes is what is committed and tested.

  python3 fixtures/make.py                      # the synthetic small sheets
  python3 fixtures/make.py real.json large.json # also convert the Cartographer prototype graphs

All sizes are in grid steps of 10 px.
Sizing rule for converted graphs (the MSAGL prototype's, so its numbers stay comparable):
  box width  = ceil((40 + max(len(name) * 6.7, (len(kind) + 12) * 5.5)) / 20) * 20 px, height 40 px
  container  = same width rule as its title strip, 20 px high
  label      = ceil(len(text) * 5.4 + 8) px wide, 12 px high, each rounded up to whole steps
"""
import json, math, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))


def width(name, kind):
    return math.ceil((40 + max(len(name) * 6.7, (len(kind) + 12) * 5.5)) / 20) * 2


def label_width(text):
    return math.ceil(math.ceil(len(text) * 5.4 + 8) / 10) if text else 0


def box(id, name, kind, parent=None, container=False):
    b = {"id": id, "width": width(name, kind), "height": 2 if container else 4}
    if parent:
        b["parent"] = parent
    return b


def link(a, b, text):
    l = {"id": f"{a}>{b}"}
    l.update({"fromEdge": a[1:]} if a.startswith("@") else {"from": a})
    l.update({"toEdge": b[1:]} if b.startswith("@") else {"to": b})
    if text:
        l.update({"labelWidth": label_width(text), "labelHeight": 2})
    return l


def write(name, step, boxes, links):
    os.makedirs(os.path.join(HERE, name), exist_ok=True)
    lines = lambda items: ",\n".join("    " + json.dumps(i, ensure_ascii=False) for i in items)
    text = '{\n  "boxes": [\n' + lines(boxes) + '\n  ],\n  "links": [\n' + lines(links) + "\n  ]\n}\n"
    with open(os.path.join(HERE, name, f"{step}.graph.json"), "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def convert(path, name):
    for step, scan in enumerate(json.load(open(path, encoding="utf-8"))["scans"], 1):
        parents = {n["parent"] for n in scan["nodes"] if n.get("parent")}
        boxes = [box(n["id"], n["name"], n["kind"], n.get("parent"), n["id"] in parents) for n in scan["nodes"]]
        write(name, step, boxes, [link(e["from"], e["to"], e.get("label", "")) for e in scan["edges"]])


class Rng:
    """A fixed generator, so the sheets do not depend on the Python version."""

    def __init__(self, seed):
        self.s = seed

    def next(self, n):
        self.s = (self.s * 6364136223846793005 + 1442695040888963407) % 2**64
        return (self.s >> 33) % n

    def pick(self, items):
        return items[self.next(len(items))]


class Sheet:
    """Components, databases holding tables, optionally servers holding databases."""

    def __init__(self, seed):
        self.r, self.boxes, self.links, self.n = Rng(seed), {}, {}, 0

    def add(self, id, kind, parent=None):
        self.boxes[id] = (kind, parent)
        return id

    def fresh(self, prefix):
        self.n += 1
        return f"{prefix}{self.n}"

    def connect(self, a, b, text):
        if a != b and f"{a}>{b}" not in self.links and f"{b}>{a}" not in self.links:
            self.links[f"{a}>{b}"] = (a, b, text)

    def of(self, kind):
        return sorted(i for i, (k, _) in self.boxes.items() if k == kind)

    def build(self, components, databases, tables, servers=0, edges=0):
        srv = [self.add(f"server{i + 1}", "server") for i in range(servers)]
        for d in range(databases):
            db = self.add(f"db{d + 1}", "database", srv[d % servers] if servers else None)
            for _ in range(tables):
                self.add(f"{db}/{self.fresh('table')}", "table", db)
        comps = [self.add(self.fresh("svc"), "component") for _ in range(components)]
        dbs = self.of("database")
        for i, c in enumerate(comps):
            # summarised links: a component to a whole database, labelled with a count
            self.connect(c, dbs[i % len(dbs)], f"{3 + self.r.next(20)} links")
            if self.r.next(2):
                self.connect(c, self.r.pick(dbs), f"{1 + self.r.next(9)} links")
            if i:
                self.connect(c, comps[self.r.next(i)], "calls")
        for i in range(1, len(dbs)):
            self.connect(dbs[i], dbs[self.r.next(i)], f"{1 + self.r.next(6)} links")
        tabs = self.of("table")
        for _ in range(len(tabs) // 3):  # a few detailed links
            self.connect(self.r.pick(comps), self.r.pick(tabs), "reads")
            a, b = self.r.pick(tabs), self.r.pick(tabs)
            if self.boxes[a][1] == self.boxes[b][1]:
                self.connect(a, b, "references")
        for i in range(edges):
            side = ["right", "left", "bottom", "top"][i % 4]
            if i % 2:
                self.connect("@" + side, self.r.pick(comps), "feeds")
            else:
                self.connect(self.r.pick(comps), "@" + side, "publishes")
        return self

    def remove(self, id):
        gone = {id} | {i for i, (_, p) in self.boxes.items() if p == id}
        for g in gone:
            del self.boxes[g]
        self.links = {k: v for k, v in self.links.items() if v[0] not in gone and v[1] not in gone}

    def churn(self):
        """What one scan typically changes: a box gone, one or two new, a link or two different."""
        leaves = self.of("table") + self.of("component")
        if len(leaves) > 3:
            self.remove(self.r.pick(leaves))
        for _ in range(1 + self.r.next(2)):
            dbs = self.of("database")
            if dbs and self.r.next(3):
                db = self.r.pick(dbs)
                new = self.add(f"{db}/{self.fresh('table')}", "table", db)
                self.connect(self.r.pick(self.of("component")), new, "writes")
            else:
                new = self.add(self.fresh("svc"), "component")
                self.connect(new, self.r.pick(dbs or self.of("component")), f"{2 + self.r.next(9)} links")
                self.connect(new, self.r.pick(self.of("component")), "calls")
        if self.links and self.r.next(2):
            del self.links[self.r.pick(sorted(self.links))]
        comps = self.of("component")
        self.connect(self.r.pick(comps), self.r.pick(comps + self.of("table")), "calls")

    def add_database(self, tables):
        db = self.add(self.fresh("newdb"), "database")
        for _ in range(tables):
            self.add(f"{db}/{self.fresh('table')}", "table", db)
        self.connect(self.r.pick(self.of("component")), db, "4 links")

    def save(self, name, step):
        parents = {p for _, p in self.boxes.values() if p}
        boxes = [box(i, i.split("/")[-1], k, p, i in parents) for i, (k, p) in self.boxes.items()]
        write(name, step, boxes, [link(a, b, t) for a, b, t in self.links.values()])


def scans(name, sheet, count=3, change=None):
    for step in range(1, count + 1):
        if step > 1:
            (change or (lambda s, _: s.churn()))(sheet, step)
        sheet.save(name, step)


def containers_change(sheet, step):
    # scan 2: a database appears with its tables; scan 3: another database disappears whole
    sheet.add_database(3) if step == 2 else sheet.remove("db1")


if __name__ == "__main__":
    scans("small-05", Sheet(5).build(components=2, databases=1, tables=2))
    scans("small-12", Sheet(12).build(components=4, databases=2, tables=3))
    scans("small-20", Sheet(20).build(components=5, databases=3, tables=4))
    scans("small-30", Sheet(30).build(components=6, databases=4, tables=5))
    scans("nested", Sheet(7).build(components=4, databases=4, tables=2, servers=2))
    scans("edges", Sheet(9).build(components=4, databases=2, tables=3, edges=4))
    scans("containers", Sheet(11).build(components=4, databases=2, tables=3), change=containers_change)
    scans("long", Sheet(13).build(components=4, databases=2, tables=3), count=10)
    for path in sys.argv[1:]:
        convert(path, os.path.splitext(os.path.basename(path))[0])

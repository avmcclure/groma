# Groma

Lays out survey plans: ruled boxes, some holding other boxes, joined by right-angled labelled lines on a grid.
Its first rule is that a box, once placed, stays where it is.

Status: under construction. Built so far: the layout file format, the quality measures and the benchmark harness. There is no layout engine yet.

```
dotnet test
dotnet run --project src/Groma.Cli -c Release -- bench fixtures
dotnet run --project src/Groma.Cli -- measure layout.json [--previous old.json]
```

- `docs/metrics.md` defines every measure the benchmark reports.
- `fixtures/` holds the benchmark graphs; `fixtures/make.py` documents how they were made.

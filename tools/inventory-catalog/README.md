# Inventory Catalog Browser

Static browser for inventory-capable clonebase items exported from the Auto Assault client `clonebase.wad`.

## Files

- `inventory-items.json` — exported catalog (generated)
- `index.html` — searchable table UI (loads JSON via `fetch`)
- `inventory-catalog-standalone.html` — single shareable file with all data embedded

## Export JSON

Catalog export previously lived in `AutoCore.Dev` (removed). Checked-in `inventory-items.json` / standalone HTML can be used as-is. To regenerate, add an export path in `AutoCore.DevTool` or re-run whatever clonebase export you prefer.

Each item includes:

- `displayName` — client `ShortDesc` (for example, `Salvaged Pneumatics`)
- `className` — clonebase type (`Commodity`, `Weapon`, `Item`, ...)
- `cbid` / `typeId` — clonebase IDs
- `uniqueName` — internal asset name (`item_res_n_pneumatics_1`)
- `maxStackSize` — normalized stack cap (`0` in WAD becomes `1`)
- `rawStackSize` — original WAD value
- `stackable`, `subType`, `commodityGroupType`, inventory footprint fields

## View in browser

**Shareable single file:** open `inventory-catalog-standalone.html` directly in any browser.

**Split JSON + HTML version:** the `index.html` page loads JSON with `fetch`, so serve the folder locally:

```powershell
cd tools/inventory-catalog
python -m http.server 8080
```

Then open `http://localhost:8080/`.

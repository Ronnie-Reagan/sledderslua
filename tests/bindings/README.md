# Current game binding contract

`current.json` records the exact managed member shapes used by the current stable framework bindings. It intentionally does **not** contain the Sledders assembly.

Validate a locally owned/current game DLL with:

```text
python tools/audit_assembly.py "C:\...\Assembly-CSharp.dll" tests/bindings/current.json
```

A passing contract means the required managed types/fields/method signatures still exist. It does not prove live semantics, units, player ownership, authority or gameplay behavior; run the in-game smoke tests as well.

When updating the contract, investigate every changed member first. Do not simply regenerate the contract to make a failing audit green.

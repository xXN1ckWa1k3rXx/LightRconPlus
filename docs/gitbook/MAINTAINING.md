# Maintaining the documentation

All reader-facing documentation is English. API names, Lua identifiers and protocol values retain their exact spelling.

`README.md` is the short Welcome page. `SUMMARY.md` defines navigation. `events.md` documents callback payloads; `variables/` contains one page per namespace and a player-reference page. Function entries use a signature, description, Name/Type/Description parameter table and return value. Examples belong beside the relevant API or in `examples/`.

This reference describes the local Pre-alpha 1.0 implementation, API version 1. Project code is not published with these documentation commits. Check signatures and behavior against that implementation before changing a page. Do not claim real-server validation based on simulator tests.

The `.gitbook.yaml.example` file is an inactive template. When connecting Git Sync, copy it to `.gitbook.yaml` at the repository root, then connect the chosen branch and preview navigation before publishing. The current commit scope remains `docs/gitbook/` only; no root configuration or GitBook site has been published.

Before committing, check relative links, SUMMARY coverage and API coverage. Avoid introducing a second independently maintained reference. The layout follows the section/function/parameter-table pattern of the archived GitBook references supplied for this project; descriptions and examples here document LightRcon+.

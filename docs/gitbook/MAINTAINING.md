# Maintaining the documentation

All reader-facing documentation is English. API names, Lua identifiers and protocol values retain their exact spelling.

`README.md` is the short Welcome page. `SUMMARY.md` defines navigation. `events.md` documents callback payloads; `variables/` contains one page per namespace and a player-reference page. Function entries use a signature, description, Name/Type/Description parameter table and return value. Examples belong beside the relevant API or in `examples/`.

This reference describes the Pre-alpha 1.0 implementation, API version 1. Check signatures and behavior against the project code before changing a page. Do not claim real-server validation based on simulator tests.

GitBook site and Git Sync settings are managed separately by the repository maintainer. The `.gitbook.yaml.example` file is an inactive template. The `gitbook-docs.yaml` file describes the documentation space rooted in this directory.

Before committing, check relative links, SUMMARY coverage and API coverage. Avoid introducing a second independently maintained reference. The layout follows the section/function/parameter-table pattern of the archived GitBook references supplied for this project; descriptions and examples here document LightRcon+.

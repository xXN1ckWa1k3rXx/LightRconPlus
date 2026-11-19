# Maintaining the GitBook documentation

## Current status

The documentation and project code share a repository. GitBook content lives in `docs/gitbook/`. The current work prepares the documentation structure; it does not publish project code or connect or publish a GitBook site.

`SUMMARY.md` defines reader navigation, and `README.md` is the documentation home page. This maintenance guide is not included in reader navigation.

## Connecting GitBook later

The `.gitbook.yaml.example` file in this directory is a configuration template and is not active.

When configuring Git Sync, copy the template to `.gitbook.yaml` at the repository root so GitBook uses `docs/gitbook/` as its content root. Connect the repository and chosen branch in GitBook, confirm the synchronization direction, and preview the home page, navigation and links before publishing.

Documentation preparation commits are restricted to `docs/gitbook/`, so no repository-root configuration has been added.

## Writing conventions

* Write all documentation in English, including headings, navigation labels and maintenance instructions. Preserve API names, Lua identifiers and protocol values exactly.
* Use a separate page for each namespace and lowercase English paths with hyphens.
* Use relative Markdown links between pages. Do not link to local disk paths or files that have not been committed.
* Add new pages to `SUMMARY.md`, then check navigation and links.
* Use `lua` code blocks for examples. Check code, parameters and return values against the implementation.
* Distinguish implemented, verified and planned behavior. Do not describe local simulator tests as live-server validation.
* Avoid maintaining duplicate documentation when incorporating existing API drafts.

## Commit scope

Commit only `docs/gitbook/` for this preparation work. Project source, tests, published binaries and changes to other documentation are outside its scope.

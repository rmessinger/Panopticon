Contributing to Panopticon
======================

This document describes contribution guidelines that are specific to Panopticon. It closely matches the guidelines defined by the [C# standard library](https://github.com/dotnet/corefx) itself

Developer Guide
--------------------
In case you want some more knowledge on how to get started with development of Panopticon please take a look at our [Developer Guide](docs/developer-guide.md). 

Coding Style Changes
--------------------

Panopticon tries to strictly match the coding style described in [Coding Style](docs/coding-style.md). We plan to do that with tooling, in a holistic way. In the meantime, please:

* **DO NOT** send PRs for style changes.

Documentation Style
--------------------

* **DO** keep documentation concise: state facts and decisions plainly rather than explaining at length. Prefer short, direct sentences over multi-clause ones.
* **DO** write impersonally, in a reference/spec style: state facts structurally (`Entry point: X`, `Default: Y`) rather than narrating what "you" do or what the software "will" do. Avoid second person ("you", "your") and first person ("we", "our"); name the subject (the tool, the config, the command) instead of the reader.
* **DO** cut filler and hedging phrases ("it's worth noting that", "in order to", "as mentioned above", "please note that") — say the thing directly instead.
* **DO NOT** use em-dashes (—). Use a comma, colon, or period, or split into two sentences instead.
* **DO** prefer plain prose, headers, tables, and lists over decorative formatting.
* **DO** avoid duplicating information already documented elsewhere, including rationale/background repeated across multiple docs; state it once in the most relevant place and link to it instead.
* **DO** reserve longer explanations for genuinely complex or ambiguous decisions; everything else should be a single clear statement.
* **DO** follow [`docs/roadmap/agent/MAINTAINING.md`](docs/roadmap/agent/MAINTAINING.md) for the stricter formatting rules that apply specifically to `docs/roadmap/`.

Pull Requests
-------------

* **DO** submit all code changes via pull requests (PRs) rather than through a direct commit. PRs will be reviewed and potentially merged by the repo maintainers after a peer review that includes at least one maintainer.
* **DO** give PRs short-but-descriptive names (e.g. "Improve code coverage for System.Console by 10%", not "Fix #1234")
* **DO** refer to any relevant issues, and include [keywords](https://help.github.com/articles/closing-issues-via-commit-messages/) that automatically close issues when the PR is merged.
* **DO** tag any users that should know about and/or review the change.
* **DO** ensure each commit successfully builds.  The entire PR must pass all tests in the Continuous Integration (CI) system before it'll be merged.
* **DO** address PR feedback in an additional commit(s) rather than amending the existing commits, and only rebase/squash them when necessary.  This makes it easier for reviewers to track changes.
* **DO** assume that ["Rebase and Merge"](https://github.com/blog/2141-squash-your-commits) will be used to merge your commit unless you request otherwise in the PR.
* **DO NOT** fix merge conflicts using a merge commit. Prefer `git rebase`.
* **DO NOT** mix independent, unrelated changes in one PR. Separate real product/test code changes from larger code formatting/dead code removal changes. Separate unrelated fixes into separate PRs, especially if they are in different assemblies.

Merging Pull Requests (for contributors with write access)
----------------------------------------------------------

* **DO** use ["Rebase and Merge"](https://github.blog/2016-09-26-rebase-and-merge-pull-requests) by default for contributions.
* **DO** use ["Squash and Merge"](https://github.com/blog/2141-squash-your-commits) in case a PR author created many small commits, which don't make a lot of sense individually.

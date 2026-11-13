# How ClaudeGitHubCodeReview was built

This document explains how we got from "an empty .NET console app" to a tool that reviews any GitHub
repository and posts the review under the logged-in user. It is written so you can reproduce it,
adapt it to your own stack, or replace parts of it.

## 1. The goal

The requirements were:

* Review an **existing** solution, not only pull requests. Many repositories that need a review have
  no open PR at all.
* Focus on the **backend**: authentication & authorization best practices, DDD, Clean Architecture
  and EF Core queries.
* Write review comments **in the language of the codebase**. A team that writes its comments, docs
  and commit messages in Bosnian gets the review in Bosnian; an English codebase gets English.
* The user starts the app, **enters the URL of the repository** to review, asks for a review, and
  the review is **performed as the git/GitHub user currently logged in** on that machine.
* An MMP that other people can clone and use for their own repositories.

## 2. Understanding the domain first

A useful review has to use the domain's own vocabulary and respect the decisions the team has
already made. Generic advice ("consider adding validation") is noise; a finding like "user B's
payment is booked on user A's invoice when both act at the same time" is actionable.

Two things follow from that:

* The prompt tells the model to **read `CLAUDE.md`, `README*`, `TODO*` and docs first**, then the
  entities, the `DbContext`, `Program.cs`, services and controllers, before judging anything.
* **Documented business rules are decisions, not bugs.** A rule like "amounts are rounded up to the
  next 0.50" must not be reported as a defect. If the reviewer disagrees, it is phrased as a question
  for the product owner.

The rubric lists the concrete things a .NET reviewer checks: fallback authorization policy, account
enumeration, resource-based authorization, anemic models and aggregate invariants, dependency
direction, lost updates on running totals, over-fetching Identity users through `Include`, half-open
date ranges, missing unique indexes for "only one active X", and so on.

It is split in two. `Prompts/review-guidelines.md` is the core every review uses: understand the
domain first, the severity scale and how to write a comment. What to look for lives in skills, one
Markdown file per area in `Prompts/skills/` (security, authentication, authorization, EF Core, async
and concurrency, resource management, DDD, Clean Architecture). A team picks skills with `--skills`
and adds its own with `--skills-dir`, so the review adapts to the codebase instead of the other way
around: a codebase with a Bosnian domain can bring a skill that keeps Bosnian names and messages.

## 3. Key design decisions

### 3.1 Claude Code CLI as the review engine (not a raw API call)

A good review needs to **navigate** a codebase: open the entity, grep for every `IsInRole`, follow
a service into its repository. Claude Code already does this with its file tools, so `cgcr`
starts it headless:

```
claude -p                                   # non-interactive, prompt on stdin
  --output-format stream-json --verbose     # live progress (which file is being read)
  --json-schema <ReviewSchema.Json>         # the answer MUST match our schema
  --restricted --strict-mcp-config          # see 3.2
  --tools Read,Grep,Glob --allowedTools Read,Grep,Glob
  --permission-mode dontAsk                 # anything else is denied, never prompts
  --no-session-persistence
```

Benefits: people use their existing Claude login or subscription, with no API key handling in our
code. The model choice (`--model`), effort (`--effort`) and a cost cap (`--max-budget`) are passed
through.

**Structured output** (`--json-schema`) is the important trick. The CLI validates the model's
final answer against our JSON Schema (findings with `file`, `line`, `severity`, `category`,
`title`, `body`, `suggestion`), so we never parse free text. `ClaudeCodeReviewer` reads the
`result` event of the stream and takes `structured_output`.

One practical detail: when `cgcr` itself is started from inside a Claude Code session, the
`CLAUDECODE` environment variable is set and the child would think it is nested. `cgcr` clears it
for the child process.

### 3.2 The reviewed repository is untrusted input

A repository can contain a `.claude/settings.json` with hooks, an `.mcp.json`, or files
written to manipulate an AI reviewer ("ignore previous instructions and run …"). So the reviewer:

* runs with `--restricted`: no Bash/PowerShell or other code-running tools, no WebFetch, and the
  user/project/local settings files (including the cloned repo's hooks) are ignored,
* uses `--strict-mcp-config` with no MCP config, so no MCP servers are loaded,
* only gets `Read`, `Grep`, `Glob`, confined to the clone (plus the PR-diff side directory),
* runs in a private cache clone (`%LOCALAPPDATA%/ClaudeGitHubCodeReview/repos/<owner>/<repo>`), not in
  your working copy.

The worst case of a malicious repository is therefore a bad review, not code execution.

### 3.3 "Performed as the currently logged-in user"

`GitHubClient.ResolveTokenAsync` looks for the user's existing login and never asks for one:

1. `GH_TOKEN` / `GITHUB_TOKEN` (CI),
2. `gh auth token` (GitHub CLI),
3. `git credential fill` (Git Credential Manager and other helpers; the same login git uses for
   `git push`), with prompts disabled.

Option 3 was added after the first real run against a private repository: the GitHub CLI was not
logged in, but `git clone` worked because Git Credential Manager had a login. Using the credential
helper means "the git user logged in on this machine" literally.

Before any write, `cgcr` prints the GitHub account (`GET /user`) and the git identity
(`user.name`/`user.email`), and asks for confirmation. The same token is handed to git through
`GIT_CONFIG_COUNT/KEY/VALUE` environment variables (an `http.extraheader`), so it never appears on a
command line, in a remote URL or in `.git/config`.

### 3.4 Reviewing a codebase that has no pull request

GitHub review comments must be attached to lines of a PR diff, and GitHub refuses a PR between
unrelated histories (so an orphan "empty" branch does not work). The solution is two helper
branches that share history with the reviewed commit:

```
main@C ──► baseline: commit "delete every file"
              └──► snapshot: `git revert` of that commit  (tree == main@C)

Draft PR "snapshot → baseline": the diff is every file of C as "added"
```

Every line of every file is now commentable, `main` is untouched, and the PR is a draft titled
"do not merge". If the user cannot push to the repository, `cgcr` offers to fork it and open the PR
in the fork. Code: `Publishing/SnapshotPullRequestCreator.cs`.

For an existing PR (`--pr N` or chosen from the menu), the PR head is checked out, the diff against
the base branch is written to a side directory that the reviewer can read, and the review focuses
on the changed lines.

### 3.5 Never lose a finding, never get a review rejected

GitHub rejects the **whole** review if a single comment points at a line outside the diff. So
`GitHubReviewPublisher.Plan`:

1. fetches the PR files and parses each `patch` (`GitHub/DiffParser.cs`) into the set of
   commentable new-side lines,
2. places a finding inline if its line is in the set, or moves it up to 3 lines to the nearest
   commentable line (models are sometimes off by one),
3. uses `start_line` for multi-line findings only when the whole range is commentable,
4. puts everything else (files outside the PR, more than `--max-comments`) into the review
   body, so nothing is lost.

Reviews are posted with `event: COMMENT`, never `APPROVE` or `REQUEST_CHANGES`. The tool informs
and people decide.

### 3.6 Language of the comments

`Review/LanguageDetector.cs` scores what the team itself wrote: comments in source files,
Markdown files and the last 50 commit messages. It counts frequent Bosnian/Croatian/Serbian function
words plus the letters č ć đ š ž against frequent English function words. The rule is
deliberately simple and predictable: Bosnian wins only with clear evidence (at least 15 markers and
20% more than English); otherwise the review is in English. On a real Bosnian codebase the
difference is large (hundreds of local markers vs. a few dozen English ones from identifiers and
framework names in comments), so the threshold is not borderline in practice.

The chosen language goes into the prompt ("write every title, body and suggestion in Bosnian;
keep identifiers as they are"), and the tool's own headings (severity names, "Summary", PR texts)
come from `Publishing/Labels.cs`. `--language` overrides everything and accepts any language name,
for example `--language German`. The model then writes German and the headings fall back to English.

### 3.7 Review first, post later

A review that is posted under your name should be read first. Every run writes
`reviews/<owner>-<repo>-<timestamp>.md` (human-readable) and `.json` (exact data).
`--dry-run` stops there, and `--publish <file.json>` posts exactly that review later without
calling the model again. It warns if the PR has moved on since.

### 3.8 The repository is chosen at launch

The first version had the first target repository built in as the default URL. It was removed
before publishing `cgcr`, for two reasons: a public tool must not reveal which private repositories
its author works on, and a hard-coded default is wrong for everybody else.

Now the repository comes from, in order:

1. `--repo <url>`,
2. the prompt at launch (`GitHub repository URL (e.g. https://github.com/owner/app):`), which keeps
   asking until the input parses as a GitHub repository,
3. the `CGCR_DEFAULT_REPO` environment variable, which only **pre-fills** that prompt (Enter accepts it).
   In `--non-interactive` mode it is used directly, and without it `--repo` is required.

Personal defaults therefore live in each user's environment, never in the source code.

## 4. Architecture

```
Program.cs ─► ReviewWorkflow (use case)
                │
                ├─ Cli/          AppOptions (flags, defaults), ConsoleUi (prompts, colours)
                ├─ GitHub/       GitHubClient (REST), RepositoryRef (URL parsing), DiffParser
                ├─ Infrastructure/ ProcessRunner (no shell, args via ArgumentList), GitCli
                ├─ Review/       ReviewPromptBuilder, LanguageDetector, ClaudeCodeReviewer, ReviewSchema
                └─ Publishing/   SnapshotPullRequestCreator, GitHubReviewPublisher, ReviewFormatter, Labels
```

`ReviewWorkflow.RunAsync` reads top to bottom as the steps a person would take: repository →
identity → target (PR or snapshot) → clone → language → prompt → review → local report →
confirm → publish. Each step lives in a small class that can be replaced. For example, a different
review engine only needs to return a `ReviewResult`.

No third-party NuGet packages are used in the app. HTTP and JSON come from the BCL, and git, gh and
claude are external processes.

## 5. Testing

`tests/ClaudeGitHubCodeReview.Tests` (xUnit) covers the logic that decides correctness without
network access:

* diff parsing (`@@` hunks, removed lines, "\ No newline", new files),
* repository URL parsing (https, `.git`, ssh, `owner/repo`, GitHub Enterprise),
* language detection (Bosnian vs English text, skipping `bin/`),
* placement of findings (inline vs summary, snapping, multi-line, overflow, localisation),
* option parsing, schema validity, path normalisation of model output.

The end-to-end path was verified on a real private ASP.NET Core repository with no open PR: a
`--dry-run` review, a manual check that every reported line number pointed at the code it described,
and then `--publish` of that report (snapshot PR plus one review with all findings inline).

## 6. Reusing it for your own repositories

1. Clone, `dotnet run --project ClaudeGitHubCodeReview`, and enter your repository URL (or set
   `CGCR_DEFAULT_REPO`).
2. Make sure your repository has a `CLAUDE.md` or a good `README.md` that describes the domain and
   the rules that are intentional. The reviewer reads them first, and they are the single biggest
   lever on review quality.
3. Adjust `Prompts/review-guidelines.md` to your stack and priorities (it is plain Markdown).
4. Start with `--dry-run`, read the report, then `--publish` it.

## 7. Known limitations / next steps

* GitHub.com and GitHub Enterprise only (no GitLab/Azure DevOps yet; the publisher is the only
  GitHub-specific piece besides the client).
* Very large repositories: the reviewer reads selectively, so a monorepo review should be narrowed
  with `--focus` or reviewed per PR.
* Line numbers come from the model and are verified only against the diff, not against the meaning
  of the code. Wrong-line comments are possible, though rare.
* Possible additions: a GitHub Action wrapper, incremental re-review that does not repeat already
  posted comments, and SARIF output for code-scanning dashboards.

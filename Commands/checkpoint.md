# /checkpoint

Use this slash command when you want to capture a snapshot of the current work.

**When invoked**
1. Review `git status` to confirm tracked/untracked files that should be included.
2. Stage the relevant files (`git add ...`).
3. Create a commit with an appropriate message summarizing the work in progress.
4. Report the resulting commit hash and status back to the user.

> After you ask me to run `/checkpoint`, I will perform the steps above to produce a git commit.

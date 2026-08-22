# Git workflow

Use this workflow so everyone on the CPTN team can review changes before they land on `master`.

## One-time setup

```bash
git clone https://github.com/jer2514/CPTN.git
cd CPTN
git checkout master
git pull origin master
```

Set your identity so commits are attributed to you (use the same email as your GitHub account):

```bash
git config --global user.name "Your Name"
git config --global user.email "you@example.com"
```

If you prefer GitHub to hide your real email, use the `noreply` address from GitHub → Settings → Emails.

## Everyday workflow

1. Update `master` before you start:

   ```bash
   git checkout master
   git pull origin master
   ```

2. Create a short-lived branch. Prefer these prefixes:

   | Prefix | Use for |
   | --- | --- |
   | `feature/` | New screens or behavior |
   | `fix/` | Bug fixes |
   | `chore/` | Git, CI, docs, cleanup |

   ```bash
   git checkout -b feature/short-description
   ```

3. Commit focused changes:

   ```bash
   git add .
   git status
   git commit -m "Describe what this commit does"
   ```

4. Push and open a pull request into `master`:

   ```bash
   git push -u origin HEAD
   ```

   Then open a PR on GitHub. Wait for the CI build to pass, then request a teammate review.

5. After the PR is merged, delete the branch and go back to `master`:

   ```bash
   git checkout master
   git pull origin master
   git branch -d feature/short-description
   ```

Do not commit directly to `master`. Do not reuse long-lived personal branches (`jer`, `jerry`, `Ralph`) for new work.

## What not to commit

- Build output (`bin/`, `obj/`) — already ignored
- Visual Studio / Rider / VS Code user settings
- Local secrets, `appsettings.*.local.json`, and personal connection strings
- User-uploaded photos under `wwwroot/Uploads/` except the project logo

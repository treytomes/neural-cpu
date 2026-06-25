---
name: commit
description: Write a journal entry, then stage and commit current changes
user-invokable: true
args: optional commit message topic/summary
---

Combines journaling with git commit workflow to create a record of both the technical changes and the thinking behind them.

## Steps

1. **First, invoke the journal skill** with the provided topic (or "commit" if none provided)
   - This creates a timestamped reflection on the session's work
   - Take time to document design decisions and insights

2. **Review current changes**
   - Run `git status` to see modified files
   - Run `git diff` to review the actual changes
   - Ensure journal entry was created

3. **Stage changes**
   - Stage relevant changed files with `git add <files>`
   - Include the new journal entry: `git add .claude/journal/{timestamp}_{topic}.md`
   - Consider whether to include other `.claude/` changes (skills, settings, etc.)

4. **Create commit**
   - Use the provided argument as a commit message hint (if provided)
   - Otherwise, derive message from the changes and journal content
   - Follow conventional commit format when appropriate: `feat:`, `fix:`, `docs:`, `refactor:`, etc.
   - Include `Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>`

5. **Verify commit**
   - Run `git status` to confirm clean working tree (or expected remaining changes)
   - Run `git log -1` to show the commit

## Commit Message Guidelines

- **First line**: Short summary (50-72 chars) of what changed
- **Body** (optional): More detailed explanation of why, any important context
- **Always include**: `Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>`

## Example Usage

```
User: /commit
→ Creates journal entry, commits all changes with auto-generated message

User: /commit clock module implementation  
→ Creates journal with topic "clock-module-implementation", commits with message derived from that context
```

## Purpose

This skill combines:
- **Documentation** (journal entry for future understanding)
- **Version control** (atomic commit of related changes)
- **Attribution** (co-authorship tracking)

The journal entry becomes part of the git history, creating a rich record of not just *what* changed but *why* and *how* decisions were made.

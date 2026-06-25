---
name: journal
description: Write a reflective journal entry about the current session
user-invokable: true
args: optional topic name (defaults to "session")
---

Create a timestamped journal entry in `.claude/journal/` reflecting on this session's work.

## Steps

1. Generate a timestamp in format `YYYY-MM-DD_HH-MM-SS`
2. Use the provided topic argument (or "session" if none provided)
3. Create a markdown file: `.claude/journal/{timestamp}_{topic}.md`
4. Fill in the journal entry with:
   - **What We Accomplished**: Concrete changes, features built, or problems solved
   - **Key Design Decisions**: Important architectural or implementation choices made
   - **Insights & Observations**: What worked well, challenges faced, interesting patterns
   - **Next Steps**: What should be tackled next or needs attention
   - **Questions & Uncertainties**: Open questions or areas needing decisions

## Journal Entry Format

```markdown
# Journal Entry - {topic}

**Date:** {current datetime}
**Session:** {brief description}

## What We Accomplished

[Describe concrete work done]

## Key Design Decisions

[Document important choices and their rationale]

## Insights & Observations

[Reflect on what worked, challenges, interesting patterns]

## Next Steps

[What comes next or needs attention]

## Questions & Uncertainties

[Open questions or undecided areas]

---
*This journal will be read by future Claude instances to understand project evolution.*
```

## Purpose

Journal entries help maintain continuity across sessions by:
- Documenting design rationale that isn't in code
- Tracking project evolution and learning
- Providing context for future Claude instances
- Recording decisions that influenced the architecture

Take time to write thoughtfully - this becomes part of the project's memory.

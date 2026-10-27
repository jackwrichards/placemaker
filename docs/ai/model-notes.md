# AI builder: models and thinking, what we saw

The same evaluation requests (`suite/scenarios/ai-eval.json`, `ai-opus.json`) on the same lands, built in the game
runner. Ratings are Claude Code's own, against the premade types and the user's grades. Transcripts:
`ModsData/Placemaker/ai/<time>/`.

| Run | Model, thinking, prompt | Per build | Steps | Cost | Rating | Notes |
|---|---|---|---|---|---|---|
| eval 1 (26 Oct) | Sonnet 5.5, default thinking, agent-6 | 60-285 s | 6-15 | $0.17-0.73 | about C | farm B, park and memorial C+, wetland got no water (anchor on an island), plaza D, two builds empty (the whole reply spent thinking) |
| eval 2 | Sonnet 5.5, low, agent-7 | 10-21 s | 3-5 | $0.06-0.12 | about D | fast but rushed: farm 8 pieces, no looking, no self-grade |
| eval 3 | Sonnet 5.5, medium, agent-7 + forced review | 27-58 s | 6-13 | $0.15-0.30 | about C- | the review was taken but light; the car park's lanes joined the streets for the first time |
| eval 4 (27 Oct) | Sonnet 5.5, medium, agent-8 + head start (essentials, examples) | 24-55 s | 4-9 | $0.12-0.29 | about C- | almost no searching any more (the essentials work); stops early; the park and plaza lost their paths under a whole-site surface (now a reported problem) |
| opus 1 | Opus 5.5, high, agent-8 + head start | about 40 s a step, the park unfinished after 11 min | 16+ | $2.40+ for the park | B on its map at step 12 | 7,000 thinking tokens of planning up front; a loop with entrances from all four streets, a central fountain plaza, a mound with a path up, a playground, dirt and meadow patches, dense planting; still a planted boundary ring. The queue's watchdog (no progress for 5x the estimate, 12.5 min here) stopped the game |

What makes the difference:
- **Effort, not knowledge.** Sonnet knows the same rules (it quotes them in its notes) but stops after a first layout;
  Opus plans the whole design before its first call and keeps building. Thinking low makes Sonnet faster and worse;
  medium is the best Sonnet trade so far.
- **The head start removed the searching** (eval 4: 1-7 searches a build instead of 5-17) but did not raise the
  ambition by itself.
- **Mechanical failures cost more than taste:** water anchored on islands, surfaces hiding paths, empty replies. Each
  is fixed in the tools' answers, not the prompt.

Open: Opus with medium thinking (the middle ground); a "keep going" mechanism for Sonnet that is not a quota.

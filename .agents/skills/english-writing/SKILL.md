---
name: english-writing
description: >-
  Write XylemUI XML docs, inline comments, test names and Preconditions/Steps/Expected remarks,
  exception messages, rules, and the README in English with the repository's terminology.
  Use when writing or editing any of these, when translating a term from the Japanese design,
  or when a review flags wording.
---

# Writing English in XylemUI

Read this before writing. When a review corrects wording, add the general rule here in the same change; do not leave it in chat. The language policy is `.cursor/rules/language.mdc`.

## Sentence shape

- Plain, direct English. Short sentences and common words.
- XML doc elements are full sentences and end with a period (SA1629).
- Summaries start with a verb in the third person: "Returns ...", "Creates ...", "Marks ...".
  - Properties: "Gets ..." or "Gets or sets ..." (SA1623).
  - Constructors: "Initializes a new instance of the <see cref="T"/> class." (SA1642).
  - Boolean members: "Gets a value indicating whether ...".
- Write `<see langword="true"/>`, `<see langword="false"/>`, `<see langword="null"/>` for keywords.
- Refer to types and members with `<see cref="..."/>` and parameters with `<paramref name="..."/>`.
- Inline comments explain why, not what the next line does. A measured reason names the measurement.
- Keep the subject the same across a method name, its summary, and its remarks.

## Test names and remarks

- `{Target}_{Scenario}_{Expected}` in PascalCase.
- Remarks use the headings Preconditions, Steps, Expected (`.cursor/rules/testing.mdc`). Name the actual input and the actual check.

## Exception messages

- One sentence that names the condition that failed, with the same terms as the design and the XML docs.

## Terminology

The left column is the term in `docs/design.ja.md`. Write the right column in code, docs, and tests.

| Design term | Write |
| --- | --- |
| Signal / Computed / Effect | signal / computed / effect |
| バッチ | batch |
| 所有権ツリー、Scope | scope (the ownership tree) |
| アリーナ、世代付きハンドル | arena, generational handle |
| 無効化、dirty 伝播 | invalidation, dirty propagation |
| Layout 境界 | layout boundary |
| 制約源 | constraint source |
| マテリアライズ、仮想化 | materialization, virtualization |
| 描画ルート | render root |
| フレーム実行サイクル | frame lifecycle |
| 同期リサイズ描画パス | synchronous resize path |
| 疑似状態 | pseudo-state |
| 見た目のない部品 | primitive (headless primitive) |
| グリフアトラス | glyph atlas |
| 縮退動作 | degradation |
| 不変条件 | invariant |
| 定常状態 | steady state |
| ホットパス、フレームの経路 | frame path |

Words to avoid:

| Avoid | Write |
| --- | --- |
| "the UI" when one node or the tree is meant | "node", "element", or "tree" |
| "fails" for throwing | "throws <see cref="InvalidOperationException"/>" (the concrete type) |
| "fast" or "zero-cost" without a number | the measurement, or nothing |

## After a review

1. Before applying a suggested replacement, look for similar sentences elsewhere (`.cursor/rules/similar-findings.mdc`).
2. If the finding is not covered above, add it here as a general rule, not as one sentence.

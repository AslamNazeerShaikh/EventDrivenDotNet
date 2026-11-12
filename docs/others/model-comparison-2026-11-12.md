# Coding Model Comparison: Flash-Class Models on OpenCode

Date: Thursday, 12 November 2026, 16:45 UTC (22:15 IST).
Location: India (IST), researched and documented from this repo.
Scope: primary coding model pick for .NET full-stack and agentic work. Prices and allowances are the OpenCode Go basic plan. Benchmarks are Artificial Analysis Intelligence Index v5.0.1 unless noted.

## Result

DeepSeek V4.1 Flash 1017 is the primary pick. Muse Spark 1.3 leads raw intelligence. Inkling 2 Small is the best second opinion on hard logic. MiniMax leads automation. MiMo is the cheapest paid per output token. Beam is free and fastest but weakest on quality. GLM keeps no outright lead once DeepSeek enters.

## Benchmarks

| # | Model | Maker | Index | AutomationBench | Terminal-Bench 4.0 | FrontierCode 2.0 | HLE Diamond | Output speed |
|---|---|---|---|---|---|---|---|---|
| 1 | Muse Spark 1.3 Contributor | Meta | 36.6 | 57.9% | 33.3% | 23.1% | 33.7% | 135 tok/s |
| 2 | DeepSeek V4.1 Flash 1017 | DeepSeek | 33 | 66.6% | 33.8% | 28.5% | 25.3% | 153 tok/s |
| 3 | Inkling 2 Small | Thinking Machines | 31 | 45.6% | 13.1% | 25.4% | 28.7% | 89 tok/s |
| 4 | MiniMax M3.1 Flash | MiniMax | 29 | 72.5% | 25.8% | 20.8% | 28.1% | 148 tok/s |
| 5 | GLM 5.3 Flash | Z.ai | 28 | 60.4% | 32.8% | 17.6% | 18.2% | 47 tok/s |
| 6 | MiMo V2.6 Flash | Xiaomi | 27 | 64.1% | 22.7% | 21.6% | 13.7% | 38 tok/s |
| 7 | Beam 1.0 | Reflection | 18 | 48.5% | 3.0% | 5.5% | 12.8% | 286 tok/s |

Notes: Muse Spark AA numbers are Standard-mode Max. Contributor mode drops max reasoning, so expect at or below Max. Inkling Terminal-Bench 2.1 is 81.3%, shown here is 4.0 only. Beam speed is end-to-end median and varies by window.

## Pricing and allowance on OpenCode Go basic plan

| # | Model | Input / output / cached per 1M | Monthly allowance | Est. requests per month | Weights |
|---|---|---|---|---|---|
| 1 | Muse Spark 1.3 Contributor | $0.10 / $0.20 / $0.002 | $60 | 226,600 | Closed, trains on prompts |
| 2 | DeepSeek V4.1 Flash 1017 | $0.15 / $0.60 off-peak, double at peak | $100 | 216,670 | Open |
| 3 | Inkling 2 Small | Free on Console, not in Go | No Go allowance | Uncapped free | Closed, Apache 2.0 promised |
| 4 | MiniMax M3.1 Flash | $0.15 / $0.60 / $0.015 | $20 | 19,380 | Closed for now |
| 5 | GLM 5.3 Flash | $0.15 / $0.50 / $0.03 | $60 | 31,580 | Open MIT |
| 6 | MiMo V2.6 Flash | $0.14 / $0.28 / $0.0028 | $60 | 150,400 | Open MIT |
| 7 | Beam 1.0 | Free on Go and Console | Unlimited | Unlimited | Open Apache 2.0 |

Context window is 1M tokens for all seven. Muse Spark max output is 131,072 per OpenCode metadata. DeepSeek max output is 384K. Peak hours for DeepSeek are 01:00-04:00 and 06:00-10:00 UTC on weekdays.

## Pick for this repo

| # | Use | Pick |
|---|---|---|
| 1 | Primary coding model | DeepSeek V4.1 Flash 1017 |
| 2 | Hard logic second opinion | Inkling 2 Small |
| 3 | Automation heavy refactors | MiniMax M3.1 Flash |
| 4 | High volume cheap edits | MiMo V2.6 Flash |
| 5 | Raw intelligence tasks | Muse Spark 1.3 Contributor |
| 6 | Free bulk drafts | Beam 1.0 |

## References

- OpenCode Go pricing and limits: https://opencode.ai/docs/console/go/
- OpenCode models endpoint: https://opencode.ai/inference/go/v1/models
- AA MiniMax M3.1 Flash: https://artificialanalysis.ai/models/minimax-m3-1-flash
- AA GLM 5.3 Flash: https://artificialanalysis.ai/models/glm-5-3-flash
- AA MiMo V2.6 Flash: https://artificialanalysis.ai/models/mimo-v2-6-flash
- AA DeepSeek V4.1 Flash 1017: https://artificialanalysis.ai/models/deepseek-v4-1-flash-1017
- AA Inkling 2 Small: https://artificialanalysis.ai/models/inkling-2-small
- AA Reflection Beam: https://artificialanalysis.ai/models/reflection-beam
- AA Muse Spark 1.3: https://artificialanalysis.ai/models/muse-spark-1-3
- DeepSeek pricing: https://api-docs.deepseek.com/quick_start/pricing/
- Meta Muse Spark docs: https://dev.meta.ai/docs/muse-spark
- Meta pricing and rate limits: https://dev.meta.ai/docs/pricing-rate-limits
- Meta announcement: https://research.meta.ai/blog/introducing-muse-spark-1-3
- Thinking Machines Inkling 2 Small: https://thinkingmachines.ai/news/inkling-2-small/
- Reflection Beam model card: https://huggingface.co/reflection/Beam-1.0-501B-A23B
- MiMo V2.6 Flash: https://mimo.mi.com/models/en-US/mimo-v2-6-flash
- Z.ai GLM 5.3 Flash: https://z.ai/blog/glm-5-3-flash
- MiniMax M3.1: https://www.minimax.io/blog/minimax-m31f

# TekstilCAD 0.2 — bağımsız gereksinim denetimi

User explicitly requested at least 50 professional agents, evidence-based missing requirements and realistic planning including future plotter connection. This is READ-ONLY application/code/data audit; each agent owns only its assigned JSON report file. Do not edit implementation, run builds/tests, interact with UI, install tools, or contact devices. Do not enumerate WearCAD broadly (about one million files). AGENTS.md protection applies. Current source root C:\Projects\TekstilCAD; current code checkpoint 059047b6a921. Source/test evidence is preferred; never infer a physical model from installed software. Do not claim source tests are production certification.

Read only relevant files for your narrow scope. Findings must distinguish proven defect, missing requirement, unverified behavior, and proposal. Do not invent observed crashes or passing tests. Maximum 4 findings per agent, focus actionable material gaps. No need to duplicate broad master-plan wishlists. Cite source file and line or exact official URL (if external fact needed use official primary documents). Unknowns must stay unknown.

Write one UTF-8 JSON file docs/audit-2026-10-31/RNN.json (your assigned number):
{
 "agent":"RNN", "role":"...", "scope":"...", "evidence":["relative/path.cs:line ..."],
 "findings":[{"id":"RNN-F1","kind":"defect|missing|unverified|proposal","priority":"P0|P1|P2|P3","title":"Turkish concise title","evidence":"exact source line/URL and fact","impact":"Turkish business/operator impact","acceptance":"measurable test/exit criterion","effort_days":[min,max],"dependencies":["named prerequisite"],"confidence":"high|medium|low"}],
 "unknowns":["..."], "recommendation":"Turkish concise recommendation"
}
Effort is preliminary engineering-person-days for your isolated work only, excludes integration/QA and MUST NOT be summed blindly across duplicated findings. P0 actual destructive release blocker, P1 pilot prerequisite, P2 paid release, P3 later. Broad absent future features are not automatically P0. Include evidence even for absence (model/API reviewed, targeted rg). Reports primarily Turkish. No markdown fences around JSON. Keep final message only report path + top finding; release concurrency slot promptly. You are not alone; don't revert or overwrite other agents.

Confirmed user steering during audit: first target LARGE FACTORY; planning team 7+ people. Plotter plugs into PC via USB (user statement), exact make/model/controller/protocol unknown. Long-term goal broad machine compatibility; require capability-based adapters and per-model qualification, never claim all machines automatically supported. Prioritize factory jobs/revisions/traceability/recovery with one qualified USB plotter first. Scope for this turn remains audit and realistic plan, no implementation changes.

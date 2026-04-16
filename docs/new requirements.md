The core question first

When you walk into an undocumented D365 environment as a consultant, what are the first 3 things you actually need to know to start working?

Not what's "nice to have" — what do you genuinely need to answer in the first hour before you can do anything useful?



Then these specific questions

On tables:

When you see a custom table you've never seen before, what tells you whether it matters or not? Is it the name, the record count, who created it, when it was last modified, whether it has active records recently?

On plugins:

When you find an undocumented plugin, what's the first question you ask? Is it "what does it do", "when does it fire", "has it ever failed", or "what will break if I disable it"?

On fields:

Have you ever been burned by changing a field that turned out to be used somewhere unexpected — a plugin, a flow, a JavaScript function, an integration? What would have saved you?

On the environment overall:

What's the single thing that's made you think "I wish I had known this on day one" after spending a week in a client's environment?

On the output format:

When you deliver documentation to a client today — manually — what do they actually read vs what do they ignore? Does a junior developer read it differently than a CTO?

On trust:

What would make you distrust an AI-generated documentation report? What would make you trust it enough to hand it directly to a client without reviewing every line?



answers from a 15 years senior technical consultant: 

What are the first 3 things I need to know in the first hour?

1\. What has been customised and by whom.

Not just what tables exist — but what was intentionally built vs what's Microsoft out-of-the-box. I need to see the publisher prefix immediately. doc\_ tells me a human made a deliberate decision. msdyn\_ tells me Microsoft shipped it. The moment I see a client's custom prefix I know where to focus. If I don't know the prefix I'm wasting time reading Microsoft's own tables.

2\. What business process does this environment actually support.

I don't need a data dictionary on day one. I need to understand — is this a sales CRM, a field service tool, a custom HR system, something unique? I can usually infer this from the custom table names and the volume of records in them. A table called vel\_equipment with 4,000 records and relationships to vel\_workorder tells me this is a field service or asset management implementation immediately. That context changes every other decision I make.

3\. Where is the dangerous code.

Plugins. Specifically — plugins that fire on high-volume tables with no filtering conditions. If I see a plugin registered on the Update message of the Contact table with no attribute filter, that plugin fires on every single contact save in the system. That's a performance time bomb and I need to know about it before I touch anything. Undocumented dangerous code is the thing that causes incidents.



What tells me a custom table matters?

Four signals in order of importance:

Record count with recency. A table with 5,000 records and the most recent one created yesterday is live and operational. A table with 5,000 records where the newest is from 2019 is probably dead data from a migration that never got cleaned up. Record count alone is misleading — I need when was the last record created or modified.

Relationships. A table that nothing points to and that points to nothing is an island. Islands are either abandoned or they're populated via an integration that bypasses the standard UI. Both are important to flag but for different reasons.

Plugin and flow coverage. If a table has three plugins registered on it and two flows watching it, it's load-bearing. Someone built significant logic around it. That table is not safe to touch without understanding all of that logic first.

Whether it appears on any form. A table with no system form is either a background data store (populated by code, never by users) or it was abandoned mid-build. That distinction matters enormously.



When I find an undocumented plugin, what's my first question?

Not "what does it do." I already know I need to read the code to answer that.

My actual first question is: "What is the blast radius if this plugin fails?"

Specifically:



Is it synchronous or asynchronous? Synchronous plugins block the user operation — if this throws an exception the user gets an error and their save fails. Asynchronous plugins fail silently in the background.

Is it registered pre-operation or post-operation? Pre-operation failures are more visible. Post-operation failures can leave data in inconsistent states.

Does it have error handling? A plugin with no try/catch that throws an unhandled exception on a synchronous pre-operation step will break the UI for every user hitting that operation. I've seen this take down sales teams for hours.

What's the filtering condition? No filter on an Update registration means it runs on every field change. That's often unintentional — the developer only meant it to fire when a specific field changed but forgot to set the filter.



If a plugin is synchronous, pre-operation, on a core table like Account or Contact, with no error handling and no attribute filter — that is a critical risk item and I flag it immediately regardless of what it does.



Have I been burned by changing a field unexpectedly?

Constantly. The worst ones are always the same pattern.

A client asks me to rename a field or change its type. I check the form — it's there. I check the views — it's there. I make the change. Then something breaks that I had no way to see from the UI:



A plugin was reading that field by logical name in hardcoded C# string — entity.GetAttributeValue<string>("old\_fieldname"). After the rename the plugin silently returns null and corrupts downstream data.

A JavaScript function had an OnChange handler registered on that field. The handler fires on a different field now or stops firing entirely depending on how the rename was handled.

A Power Automate flow had that field in a condition — if field equals X then... — and after the type change the comparison stopped working silently.

An integration was writing to that field via the Web API using the logical name. The integration started failing with a 400 error that nobody noticed for a week because errors went to a mailbox nobody monitored.



What would have saved me is exactly what Mode 2 is designed to produce — a complete list of everything that references this field before I touch it. If I had walked in with that list I would have coordinated the change across all four touchpoints simultaneously instead of discovering them one by one after the damage was done.



What's the one thing that made me say "I wish I knew this on day one"?

Every time — without exception — it's understanding which customisations were intentional vs accidental.

D365 environments accumulate technical debt in a specific way. A developer builds something, realises it doesn't work, abandons it but doesn't delete it. Another developer comes along, sees the abandoned thing, doesn't know if it's used, leaves it. This repeats for five years. By the time I arrive the environment is full of:



Plugins that are registered but their assembly is from 2018 and nobody knows if they still run

Flows that are disabled but nobody knows if disabling them was intentional

Fields that exist on no form and no view — orphaned data that something somewhere might still be writing to

Security roles that were copied from System Administrator and never properly scoped down — everyone has more access than they should

Tables with zero records that were created for a project that got cancelled



None of this is documented. None of it is obvious. And every one of these is a risk item. The abandoned plugin might still fire. The disabled flow might be disabled for a reason nobody remembers. The orphaned field might be written to by an integration. The over-privileged security role is a compliance problem.

A good audit tells me not just what exists, but what looks abandoned, risky, or inconsistent — and gives me a reason to have a conversation with the client about it.



What do clients actually read vs ignore?

Dead honest answer — clients read three things:

The executive summary. Specifically the complexity rating and the headline numbers. "Your environment has 47 custom tables, 23 plugins, and 31 active flows" — that lands immediately. They understand it. A CTO reads this and now has a mental model of what they're managing.

The recommendations section. Especially anything flagged as Critical or High risk. Clients read risk items because they're actionable. "This plugin will crash your sales team's save operation if it throws an exception" gets read. "Here is a description of the field data types on the Contact table" does not.

The section about whatever they're worried about. If they brought me in because something is slow, they read the performance section. If they're worried about security, they read the security roles section. Everything else is reference material they'll come back to if they need it.

What they ignore — field-by-field schema dumps. Nobody reads 200 rows of field names and data types. That content belongs in an appendix or a searchable reference, not the main document.

Junior developers read the technical details — plugin logic, field schemas, relationship maps. They're using it as a reference while they work.

Senior stakeholders read the summary and the risks. They want to know what's broken and what it's going to cost to fix.

This means the document needs two modes within the same output — an executive layer that tells the story in plain English, and a technical reference layer that developers can dig into.



What would make me distrust an AI-generated report?

Three things immediately destroy my trust:

Hallucinated specifics. If the report says "this plugin sends an email notification to the sales team" and I look at the code and it does no such thing — the whole report is suspect. I'd have to verify every single finding manually. That's worse than no report.

Generic advice that doesn't reference the actual environment. "Consider adding descriptions to your custom tables" — if that applies to every D365 environment ever, it tells me the AI wasn't actually reading my environment. Real insight is specific. "doc\_testtable has no description, no records, and no relationships — this appears to be an abandoned development artifact from \[date of last modified]" — that's a real finding.

Missing things I can see myself. If I glance at the environment, notice an obvious plugin on the Account table, and the report doesn't mention it — I'm done trusting it. Coverage gaps are fatal to trust.



What would make me trust it enough to hand it to a client?

Specificity. Every finding references a real named entity — not "some plugins have no error handling" but "EquipmentStatusPlugin (registered on vel\_equipment Update, pre-operation, synchronous) has no try/catch block."

Honest uncertainty. If the tool can't decompile a plugin, say so. "Plugin assembly could not be analysed — manual review required." Don't fabricate. Don't skip it silently. Acknowledge the gap.

A confidence indicator. Something like "This finding is based on static code analysis" vs "This finding is inferred from table structure" — so I know which conclusions are solid and which are educated guesses.

Actionable recommendations, not observations. Don't just say "this plugin has no error handling." Say "this plugin is registered synchronously on the Account Update message. If it throws an unhandled exception, every Account save in the system will fail for all users. Recommended action: wrap the Execute method in a try/catch and implement a logging strategy before this environment goes to production."

If the report reads like something a senior consultant actually wrote — specific, referenced, actionable, honest about gaps — I hand it to a client. If it reads like a data export with a summary paragraph, I don't.



What this means for the product

Reading back through all of this, the gaps in what we have vs what's needed are clear:

We're missing:



Last modified date on tables and records — not just count, but recency

Plugin risk classification — synchronous vs async, pre vs post, filtered vs unfiltered, error handling present or absent

Abandoned artifact detection — disabled flows, zero-record tables, orphaned fields, unused plugins

Specificity in recommendations — every recommendation must name the exact entity it applies to

A confidence layer — distinguish code-based findings from inferred findings

Two-layer output — executive narrative + technical reference appendix



We have:



The right architecture to deliver all of this

The right AI to generate the specific, referenced language

The right data access to pull every signal we needWhat are the first 3 things I need to know in the first hour?

1\. What has been customised and by whom.

Not just what tables exist — but what was intentionally built vs what's Microsoft out-of-the-box. I need to see the publisher prefix immediately. doc\_ tells me a human made a deliberate decision. msdyn\_ tells me Microsoft shipped it. The moment I see a client's custom prefix I know where to focus. If I don't know the prefix I'm wasting time reading Microsoft's own tables.

2\. What business process does this environment actually support.

I don't need a data dictionary on day one. I need to understand — is this a sales CRM, a field service tool, a custom HR system, something unique? I can usually infer this from the custom table names and the volume of records in them. A table called vel\_equipment with 4,000 records and relationships to vel\_workorder tells me this is a field service or asset management implementation immediately. That context changes every other decision I make.

3\. Where is the dangerous code.

Plugins. Specifically — plugins that fire on high-volume tables with no filtering conditions. If I see a plugin registered on the Update message of the Contact table with no attribute filter, that plugin fires on every single contact save in the system. That's a performance time bomb and I need to know about it before I touch anything. Undocumented dangerous code is the thing that causes incidents.



What tells me a custom table matters?

Four signals in order of importance:

Record count with recency. A table with 5,000 records and the most recent one created yesterday is live and operational. A table with 5,000 records where the newest is from 2019 is probably dead data from a migration that never got cleaned up. Record count alone is misleading — I need when was the last record created or modified.

Relationships. A table that nothing points to and that points to nothing is an island. Islands are either abandoned or they're populated via an integration that bypasses the standard UI. Both are important to flag but for different reasons.

Plugin and flow coverage. If a table has three plugins registered on it and two flows watching it, it's load-bearing. Someone built significant logic around it. That table is not safe to touch without understanding all of that logic first.

Whether it appears on any form. A table with no system form is either a background data store (populated by code, never by users) or it was abandoned mid-build. That distinction matters enormously.



When I find an undocumented plugin, what's my first question?

Not "what does it do." I already know I need to read the code to answer that.

My actual first question is: "What is the blast radius if this plugin fails?"

Specifically:



Is it synchronous or asynchronous? Synchronous plugins block the user operation — if this throws an exception the user gets an error and their save fails. Asynchronous plugins fail silently in the background.

Is it registered pre-operation or post-operation? Pre-operation failures are more visible. Post-operation failures can leave data in inconsistent states.

Does it have error handling? A plugin with no try/catch that throws an unhandled exception on a synchronous pre-operation step will break the UI for every user hitting that operation. I've seen this take down sales teams for hours.

What's the filtering condition? No filter on an Update registration means it runs on every field change. That's often unintentional — the developer only meant it to fire when a specific field changed but forgot to set the filter.



If a plugin is synchronous, pre-operation, on a core table like Account or Contact, with no error handling and no attribute filter — that is a critical risk item and I flag it immediately regardless of what it does.



Have I been burned by changing a field unexpectedly?

Constantly. The worst ones are always the same pattern.

A client asks me to rename a field or change its type. I check the form — it's there. I check the views — it's there. I make the change. Then something breaks that I had no way to see from the UI:



A plugin was reading that field by logical name in hardcoded C# string — entity.GetAttributeValue<string>("old\_fieldname"). After the rename the plugin silently returns null and corrupts downstream data.

A JavaScript function had an OnChange handler registered on that field. The handler fires on a different field now or stops firing entirely depending on how the rename was handled.

A Power Automate flow had that field in a condition — if field equals X then... — and after the type change the comparison stopped working silently.

An integration was writing to that field via the Web API using the logical name. The integration started failing with a 400 error that nobody noticed for a week because errors went to a mailbox nobody monitored.



What would have saved me is exactly what Mode 2 is designed to produce — a complete list of everything that references this field before I touch it. If I had walked in with that list I would have coordinated the change across all four touchpoints simultaneously instead of discovering them one by one after the damage was done.



What's the one thing that made me say "I wish I knew this on day one"?

Every time — without exception — it's understanding which customisations were intentional vs accidental.

D365 environments accumulate technical debt in a specific way. A developer builds something, realises it doesn't work, abandons it but doesn't delete it. Another developer comes along, sees the abandoned thing, doesn't know if it's used, leaves it. This repeats for five years. By the time I arrive the environment is full of:



Plugins that are registered but their assembly is from 2018 and nobody knows if they still run

Flows that are disabled but nobody knows if disabling them was intentional

Fields that exist on no form and no view — orphaned data that something somewhere might still be writing to

Security roles that were copied from System Administrator and never properly scoped down — everyone has more access than they should

Tables with zero records that were created for a project that got cancelled



None of this is documented. None of it is obvious. And every one of these is a risk item. The abandoned plugin might still fire. The disabled flow might be disabled for a reason nobody remembers. The orphaned field might be written to by an integration. The over-privileged security role is a compliance problem.

A good audit tells me not just what exists, but what looks abandoned, risky, or inconsistent — and gives me a reason to have a conversation with the client about it.



What do clients actually read vs ignore?

Dead honest answer — clients read three things:

The executive summary. Specifically the complexity rating and the headline numbers. "Your environment has 47 custom tables, 23 plugins, and 31 active flows" — that lands immediately. They understand it. A CTO reads this and now has a mental model of what they're managing.

The recommendations section. Especially anything flagged as Critical or High risk. Clients read risk items because they're actionable. "This plugin will crash your sales team's save operation if it throws an exception" gets read. "Here is a description of the field data types on the Contact table" does not.

The section about whatever they're worried about. If they brought me in because something is slow, they read the performance section. If they're worried about security, they read the security roles section. Everything else is reference material they'll come back to if they need it.

What they ignore — field-by-field schema dumps. Nobody reads 200 rows of field names and data types. That content belongs in an appendix or a searchable reference, not the main document.

Junior developers read the technical details — plugin logic, field schemas, relationship maps. They're using it as a reference while they work.

Senior stakeholders read the summary and the risks. They want to know what's broken and what it's going to cost to fix.

This means the document needs two modes within the same output — an executive layer that tells the story in plain English, and a technical reference layer that developers can dig into.



What would make me distrust an AI-generated report?

Three things immediately destroy my trust:

Hallucinated specifics. If the report says "this plugin sends an email notification to the sales team" and I look at the code and it does no such thing — the whole report is suspect. I'd have to verify every single finding manually. That's worse than no report.

Generic advice that doesn't reference the actual environment. "Consider adding descriptions to your custom tables" — if that applies to every D365 environment ever, it tells me the AI wasn't actually reading my environment. Real insight is specific. "doc\_testtable has no description, no records, and no relationships — this appears to be an abandoned development artifact from \[date of last modified]" — that's a real finding.

Missing things I can see myself. If I glance at the environment, notice an obvious plugin on the Account table, and the report doesn't mention it — I'm done trusting it. Coverage gaps are fatal to trust.



What would make me trust it enough to hand it to a client?

Specificity. Every finding references a real named entity — not "some plugins have no error handling" but "EquipmentStatusPlugin (registered on vel\_equipment Update, pre-operation, synchronous) has no try/catch block."

Honest uncertainty. If the tool can't decompile a plugin, say so. "Plugin assembly could not be analysed — manual review required." Don't fabricate. Don't skip it silently. Acknowledge the gap.

A confidence indicator. Something like "This finding is based on static code analysis" vs "This finding is inferred from table structure" — so I know which conclusions are solid and which are educated guesses.

Actionable recommendations, not observations. Don't just say "this plugin has no error handling." Say "this plugin is registered synchronously on the Account Update message. If it throws an unhandled exception, every Account save in the system will fail for all users. Recommended action: wrap the Execute method in a try/catch and implement a logging strategy before this environment goes to production."

If the report reads like something a senior consultant actually wrote — specific, referenced, actionable, honest about gaps — I hand it to a client. If it reads like a data export with a summary paragraph, I don't.



What this means for the product

Reading back through all of this, the gaps in what we have vs what's needed are clear:

We're missing:



Last modified date on tables and records — not just count, but recency

Plugin risk classification — synchronous vs async, pre vs post, filtered vs unfiltered, error handling present or absent

Abandoned artifact detection — disabled flows, zero-record tables, orphaned fields, unused plugins

Specificity in recommendations — every recommendation must name the exact entity it applies to

A confidence layer — distinguish code-based findings from inferred findings

Two-layer output — executive narrative + technical reference appendix



We have:



The right architecture to deliver all of this

The right AI to generate the specific, referenced language

The right data access to pull every signal we need


# Private planning scenarios

Future plan is separate from the transaction ledger. It helps compare estimates before income or actual spending is supplied. It never creates received income, expenses, savings transfers, goals, investments or a carry-forward balance.

## Original and current figures

A local profile contains `original` and `current` planning definitions. The original full-month current-cost comparison is preserved after updates. Each save copies the previous valid profile into `planning-history` before atomically replacing the current file. Changing the original baseline through the app is rejected.

Keep the JSON file and its history private. The app reads `%LOCALAPPDATA%\KhaiFaw\MyBudget\planning-profile.json`; an isolated `MYBUDGET_DATA_DIRECTORY` changes both the database and profile directory. These files are not packaged with the executable, exported to transaction CSV or included in a database backup.

## Calculation rules

- Full-month take-home stays provisional until a received full-month payslip with an actual net amount is supplied. Basic salary is informative; no payroll deductions are guessed.
- The employment-start month uses its separately supplied cautious envelope. It is not calculated from employment days or statutory payroll rules.
- All listed commitments and funds are retained in the first month. Variable work costs also remain at their full supplied caps; they are not silently prorated.
- The full-month template retains the supplied food base. A dated first-month scenario calculates daily food over the whole calendar month. The difference draws from the buffer first; any excess reduces the emergency-savings remainder. A shorter month leaves the unused food provision in the buffer.
- Current subscription cost, a proposed amount/start month and a confirmed billed amount/start month are separate. A proposal is never treated as confirmation. Dated scenarios before the change retain the current cost.
- Emergency savings is the envelope remainder after known allocations. Unknown costs are excluded from arithmetic only because their amounts are absent, not because they are assumed zero. The view warns that they can consume this apparent balance.
- Needs/wants/savings/goal/buffer percentages describe the known allocations over planned income. They do not force a 50/30/20 budget or claim to measure spending. Future essential-cost funds belong to needs; an unassigned buffer is not automatically savings.
- Only recurring take-home above the separate threshold uses the additional-income rule: 50% emergency/investment allocation, 30% goals, 20% lifestyle. Cent rounding retains the exact extra total. The first share stays with emergency savings until an investment destination is chosen.
- Known due dates before a supplied first payday are flagged. With an unknown payday or due date, the screen requests confirmation and warns that opening cash may be needed; it never invents a date or starting balance.
- Actual deductions and spending remain null/blank until supplied. Gross-minus-net is calculated only from two actual payslip amounts. A complete itemized breakdown must reconcile to that difference. A partial payslip does not replace recurring full-month net pay.
- If known allocations exceed income, a funding gap is shown and fixed commitments are not silently cut.

## Profile format

Schema version 1 uses camel-case JSON property names, ISO calendar dates, numeric decimal money and string enums. `original` and `current` must both be complete definitions, initially identical. The relevant fields are:

| Field | Purpose |
|---|---|
| `schemaVersion`, `recordedOn`, `currencyCode` | Version, baseline date and display currency |
| `basicSalary`, `provisionalTakeHome` | Stated basic salary and provisional full-month net envelope |
| `employmentStart`, `firstMonthEnvelope` | Employment-start date and separately supplied first-month envelope |
| `emergencySavingsTarget`, `surplusThreshold` | Core target and the distinct recurring-extra-income threshold |
| `proposedSubscriptionAmount`, `proposedSubscriptionFrom` | Optional proposal; both supplied together |
| `costs` | Unique IDs, names, nullable amounts, bucket and explanatory basis |
| `unconfirmedItems`, `context` | Outstanding questions and user-supplied context |
| `payslip`, `actualSpending` | Optional supplied actuals; not inferred from estimates |
| `firstPayday` | Nullable confirmed date used for first-month warnings |
| `confirmedSubscriptionAmount`, `confirmedSubscriptionFrom` | Actual bill and effective month; both supplied together |

Each cost uses bucket `Needs`, `Wants`, `Savings`, `Goals` or `Buffer`. Purpose is `Ordinary`, `Food`, `Subscription`, `Lifestyle`, `Buffer` or `EssentialFund`. Optional `fixedCommitment`, `firmPriority`, `dueDay` and `dailyRate` explain commitments and calendar treatment. Food and future essential-cost funds must be needs, subscription/lifestyle wants, and buffer unassigned. Amount `null` means unknown; amount `0` is an explicitly supplied zero. `EssentialFund` allows the view to distinguish ordinary essential commitments from car/medical sinking funds while keeping both in the needs percentage.

The app supports one subscription, lifestyle and buffer purpose per definition. IDs `emergency-savings`, `additional-goals` and `additional-lifestyle` are reserved for calculated rows. Profile imports validate these rules and cannot replace a different existing original baseline.

Use **Update estimates or supply a real payslip** to change the envelopes, confirm a bill/payday or enter real deduction figures. The original column and private history remain available. Spending still belongs in Transactions rather than in a proposed scenario.

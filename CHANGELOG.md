# Changelog / Αλλαγές

## 0.2.0 — Terminal prototype / Διαδραστικό περιβάλλον

- KALYPSIS navy terminal theme, clear screens per action, colored [+] creation actions and table borders.
- Office overview with real record counts; paged lists, keyword search and record opening by code.
- Per-field details, wrapping, narrow-terminal layouts, colored statuses and red/white financial warnings.
- Interactive history, policy renewal/cancellation and safer destructive-action previews.
- JSON, redirected text and CSV remain uncolored. NO_COLOR and TERM=dumb are respected.
- Policy credit notes: partial/full gross and net amounts, over-credit protection, customer credit balances, audit history, voiding and filtered CSV export.
- Automatic backup and transactional upgrade of existing 0.1.0 databases to schema 2.

Μπλε περιβάλλον KALYPSIS με χωριστές οθόνες, χρωματιστές ενέργειες, πίνακες,
σελιδοποίηση, αναζήτηση και καρτέλες. Κόκκινες ενδείξεις για ανεξόφλητα,
ακυρώσεις και πιστωτικές/αρνητικές τιμές όπου εμφανίζονται, με λευκό κείμενο.
Οι υπάρχουσες εγγραφές διατηρούνται. Προστέθηκαν πιστωτικά συμβολαίων, έλεγχοι ποσών,
ακύρωση με ιστορικό και αυτόματο αντίγραφο ασφαλείας πριν την αναβάθμιση της βάσης.

## 0.1.0 — Initial release / Αρχική έκδοση

- Standalone local C#/.NET backoffice with SQLite.
- Customers, insurers, partners, policies, renewals and receipts.
- Partial receipts, office commission recognition and relationship-protected deletion.
- Financial summary per currency, audit snapshots, filtered CSV export and database backup.
- Greek interactive menu; commands and JSON output for automation.
- Self-contained Windows x64 distribution; Greek and English guides.

Αυτόνομο τοπικό MVP backoffice με πελάτες, εταιρείες, συνεργάτες, συμβόλαια,
ανανεώσεις, εισπράξεις, οικονομική σύνοψη, ιστορικό, εξαγωγές και αντίγραφα ασφαλείας.

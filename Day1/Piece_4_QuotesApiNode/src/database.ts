import Database from "better-sqlite3";

const db = new Database("quotes.db");

db.exec(`
  CREATE TABLE IF NOT EXISTS quotes (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    author TEXT NOT NULL,
    text TEXT NOT NULL
  )
`);

export default db;
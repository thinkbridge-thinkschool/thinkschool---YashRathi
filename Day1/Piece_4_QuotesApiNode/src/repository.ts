import db from "./database.js";
import type { Quote } from "./types.js";

export class QuoteRepository {
  getAll(page: number, size: number): Quote[] {
    const offset = (page - 1) * size;

    const statement = db.prepare(`
      SELECT * FROM quotes
      LIMIT ? OFFSET ?
    `);

    return statement.all(size, offset) as Quote[];
  }

  getById(id: number): Quote | undefined {
    const statement = db.prepare(`
      SELECT * FROM quotes
      WHERE id = ?
    `);

    return statement.get(id) as Quote | undefined;
  }

  create(author: string, text: string): Quote {
    const statement = db.prepare(`
      INSERT INTO quotes(author, text)
      VALUES (?, ?)
    `);

    const result = statement.run(author, text);

    return {
      id: Number(result.lastInsertRowid),
      author,
      text
    };
  }

  delete(id: number): boolean {
    const statement = db.prepare(`
      DELETE FROM quotes
      WHERE id = ?
    `);

    const result = statement.run(id);

    return result.changes > 0;
  }
}
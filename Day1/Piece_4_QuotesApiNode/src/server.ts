import http from "node:http";
import { URL } from "node:url";
import pino from "pino";

import { QuoteRepository } from "./repository.js";
import type { CreateQuoteRequest } from "./types.ts";
import db from "./database.js";

const logger = pino();

const repository = new QuoteRepository();

const server = http.createServer(async (request, response) => {
  const url = new URL(request.url ?? "", `http://${request.headers.host}`);

  logger.info({
    method: request.method,
    path: url.pathname
  });

  request.on("aborted", () => {
    logger.warn("Request aborted");
  });

  try {
    // GET /api/quotes?page=N&size=N
    if (
      request.method === "GET" &&
      url.pathname === "/api/quotes"
    ) {
      const page = Number(url.searchParams.get("page") ?? "1");

      const size = Number(url.searchParams.get("size") ?? "10");

      const quotes = repository.getAll(page, size);

      response.writeHead(200, {
        "Content-Type": "application/json"
      });

      response.end(JSON.stringify(quotes));

      return;
    }

    // GET /api/quotes/:id
    if (
      request.method === "GET" &&
      url.pathname.startsWith("/api/quotes/")
    ) {
      const id = Number(url.pathname.split("/")[3]);

      const quote = repository.getById(id);

      if (!quote) {
        response.writeHead(404);

        response.end();

        return;
      }

      response.writeHead(200, {
        "Content-Type": "application/json"
      });

      response.end(JSON.stringify(quote));

      return;
    }

    // POST /api/quotes
    if (
      request.method === "POST" &&
      url.pathname === "/api/quotes"
    ) {
      let body = "";

      for await (const chunk of request) {
        body += chunk;
      }

      const data = JSON.parse(body) as Partial<CreateQuoteRequest>;

      const errors: Record<string, string[]> = {};

      if (!data.author?.trim()) {
        errors.author = ["Author is required"];
      }

      if (!data.text?.trim()) {
        errors.text = ["Text is required"];
      }

      if (Object.keys(errors).length > 0) {
        response.writeHead(400, {
          "Content-Type": "application/json"
        });

        response.end(JSON.stringify({
          title: "Validation Failed",
          errors
        }));

        return;
      }

      const quote = repository.create(
        data.author!,
        data.text!
      );

      response.writeHead(201, {
        "Content-Type": "application/json"
      });

      response.end(JSON.stringify(quote));

      return;
    }

    // DELETE /api/quotes/:id
    if (
      request.method === "DELETE" &&
      url.pathname.startsWith("/api/quotes/")
    ) {
      const id = Number(url.pathname.split("/")[3]);

      const deleted = repository.delete(id);

      if (!deleted) {
        response.writeHead(404);

        response.end();

        return;
      }

      response.writeHead(204);

      response.end();

      return;
    }

    response.writeHead(404);

    response.end();
  }
  catch (error) {
    logger.error(error);

    response.writeHead(500, {
      "Content-Type": "application/json"
    });

    response.end(JSON.stringify({
      title: "Server Error"
    }));
  }
});

server.listen(3000, () => {
  logger.info("Server running on http://localhost:3000");
});

process.on("SIGINT", () => {
  logger.info("Shutting down server");

  server.close(() => {
    db.close();

    logger.info("Database connection closed");

    process.exit(0);
  });
});
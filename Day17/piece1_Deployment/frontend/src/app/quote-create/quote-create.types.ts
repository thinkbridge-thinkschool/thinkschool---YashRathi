export interface CreateQuotePayload {
  author: string;
  text: string;
}

export interface CreateQuoteResponse {
  id: number;
  author: string;
  text: string;
  createdAt: string;
}

export interface Quote {
  id: number;
  author: string;
  text: string;
}

export interface CreateQuoteRequest {
  author: string;
  text: string;
}
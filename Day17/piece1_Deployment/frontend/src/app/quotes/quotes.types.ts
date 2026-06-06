export interface QuoteListItem {
  id: number;
  author: string;
  text: string;
  createdAt: string;
}

export interface QuoteDetail extends QuoteListItem {}

import { api, post, type Evaluation } from "../../../shared/services/api";
export const analyzeTransaction = (request: unknown) =>
  post<Evaluation>("/transactions", request);
export const submitReview = (id: string, isFraud: boolean, notes: string) =>
  post("/transactions/" + id + "/reviews", { isFraud, notes });
export const getTransaction = (id: string) =>
  api<Evaluation>("/transactions/" + id);

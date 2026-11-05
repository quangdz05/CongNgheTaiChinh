import { post } from "../../../shared/services/api";
export const compareTransaction = (id: string) =>
  post<any>("/compare/" + id, {});

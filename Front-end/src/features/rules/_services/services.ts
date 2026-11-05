import { api, type Policy } from "../../../shared/services/api";
export const savePolicy = (policy: Policy) =>
  api<Policy>("/rules", { method: "PUT", body: JSON.stringify(policy) });

import { post } from "../../../shared/services/api";
export const login = (username: string, password: string) =>
  post<{ token: string; username: string; role: string }>("/auth/login", {
    username,
    password,
  });

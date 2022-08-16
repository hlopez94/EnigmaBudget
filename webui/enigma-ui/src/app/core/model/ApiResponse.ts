import { ApiError } from "./ApiError";

export interface ApiResponse<T> {
  ok: boolean;
  result: T;
  errorText: string;
  errors: ApiError[];
}

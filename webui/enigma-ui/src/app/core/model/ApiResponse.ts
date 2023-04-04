import { ApiError } from "./ApiError";

export interface ApiResponse<T> {
  isSuccess: boolean;
  data: T;
  errorText: string;
  errors: ApiError[];
}

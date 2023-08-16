import { ApiError } from "./ApiError";

export interface ApiResponse {
  isSuccess: boolean;
  errorText: string;
  errors: ApiError[];
}

export interface TypedApiResponse<T> extends ApiResponse{
  data: T;
}

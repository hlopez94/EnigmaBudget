import { ApiError } from "./ApiError";

export interface ApiResponse {
  isSuccess: boolean;
  errorsText: string;
  errors: ApiError[];
}

export interface TypedApiResponse<T> extends ApiResponse{
  data: T;
}

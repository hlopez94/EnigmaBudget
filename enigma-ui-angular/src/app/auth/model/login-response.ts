export interface LoginResponse {
  loggedIn: boolean;
  jwt: string;
  userName: string;
  email: string;
  reason: string;
}

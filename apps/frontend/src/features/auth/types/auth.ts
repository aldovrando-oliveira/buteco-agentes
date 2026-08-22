export interface LoginInput {
  username: string;
  password: string;
}

export interface LoginResult {
  token: string;
  expiresAt: string;
}

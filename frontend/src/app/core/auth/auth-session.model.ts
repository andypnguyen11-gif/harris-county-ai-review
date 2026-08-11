/**
 * The signed-in user and the bearer token used to call the API.
 *
 * The shape mirrors the API's dev-token response, which in turn mirrors the
 * claims an Entra ID token carries (`preferred_username`, `name`, `roles`), so
 * swapping the sign-in source later does not change anything downstream of
 * this model.
 */
export interface AuthSession {
  accessToken: string;
  tokenType: string;
  /** ISO 8601 instant at which the token stops being accepted. */
  expiresAt: string;
  username: string;
  displayName: string;
  roles: string[];
}

/** Request body for the local-development token endpoint. */
export interface DevTokenRequest {
  username: string;
}

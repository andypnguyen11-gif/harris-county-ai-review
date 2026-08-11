/**
 * The application roles, mirroring the API's ApplicationRoles constants. Roles
 * arrive as claims on the validated token; the client only ever reads them to
 * decide what to show. Every access decision is made again server-side.
 */
export const ApplicationRoles = {
  /** Case review: cases, documents, validation, question answering. */
  Reviewer: 'Reviewer',
  /** Everything a reviewer can do, plus reference corpus administration. */
  Administrator: 'Administrator',
} as const;

export type ApplicationRole = (typeof ApplicationRoles)[keyof typeof ApplicationRoles];

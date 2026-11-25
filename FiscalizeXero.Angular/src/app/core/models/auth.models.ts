export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  userId: number;
  fullName: string;
  email: string | null;
  emailConfirmed: boolean;
  token: string;
  tokeExpiry: string;
  /** Null when the platform sign-in has no company yet; the user then chooses one on /select-company. */
  activeCompany?: { id: number; name: string } | null;
  /** The Xero connection of the active company, when it has one. `id` is the Xero organisation (tenant) id. */
  xeroOrganisation?: { id: string; name?: string | null; isSetupComplete: boolean } | null;
  /** Where "Open Xero" goes. */
  xeroUrl?: string | null;
}

export interface RegisterRequest {
  fullName: string;
  email: string;
  password: string;
}

import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { AuthUtils } from 'app/core/auth/auth.utils';
import { UserService } from 'app/core/user/user.service';
import { Observable, of, switchMap, throwError } from 'rxjs';

const API_BASE = 'http://localhost:5000/api';
const ALLOWED_ROLES = ['mercadeo precios', 'administrator'];

@Injectable({ providedIn: 'root' })
export class AuthService {
    private _authenticated: boolean = false;
    private _httpClient = inject(HttpClient);
    private _userService = inject(UserService);

    // -----------------------------------------------------------------------------------------------------
    // @ Accessors
    // -----------------------------------------------------------------------------------------------------

    /**
     * Setter & getter for access token
     */
    set accessToken(token: string) {
        localStorage.setItem('accessToken', token);
    }

    get accessToken(): string {
        return localStorage.getItem('accessToken') ?? '';
    }

    // -----------------------------------------------------------------------------------------------------
    // @ Public methods
    // -----------------------------------------------------------------------------------------------------

    /**
     * Request OTP code to be sent to the email.
     *
     * @param email
     */
    requestOtp(email: string): Observable<any> {
        return this._httpClient.post(`${API_BASE}/auth/otp`, { email });
    }

    /**
     * Compatibility method used by template modules.
     * Internally maps to OTP request.
     */
    forgotPassword(email: string): Observable<any> {
        return this.requestOtp(email);
    }

    /**
     * Compatibility method used by template modules.
     * Not used in OTP flow.
     */
    resetPassword(password: string): Observable<any> {
        return of({ success: true });
    }

    /**
     * Sign in (OTP-only). The "password" field is treated as the OTP code.
     *
     * @param credentials
     */
    signIn(credentials: { email: string; password: string }): Observable<any> {
        // Throw error, if the user is already logged in
        if (this._authenticated) {
            return throwError('User is already logged in.');
        }

        return this._httpClient.post(`${API_BASE}/auth/sign-in`, credentials).pipe(
            switchMap((response: any) => {
                const token = (response?.accessToken ?? '').toString();

                if (!token) {
                    return throwError(() => ({
                        error: {
                            message: 'No se recibio token de acceso.',
                        },
                    }));
                }

                const access = this._getAccessState(token);
                if (!access.allowed) {
                    this.signOut();

                    return throwError(() => ({
                        error: {
                            message:
                                'Acceso denegado. Solo los roles Mercadeo precios o Administrator pueden ingresar.',
                        },
                    }));
                }

                // Store the access token in the local storage
                this.accessToken = token;
                if (response.refreshToken) {
                    localStorage.setItem('refreshToken', response.refreshToken);
                }

                // Set the authenticated flag to true
                this._authenticated = true;

                // Store the user on the user service
                const email =
                    (access.claims?.email as string | undefined) ||
                    (access.claims?.preferred_username as string | undefined) ||
                    (access.claims?.upn as string | undefined) ||
                    credentials.email;

                this._userService.user = {
                    id: email,
                    name:
                        (access.claims?.name as string | undefined) ||
                        email.split('@')[0] ||
                        email,
                    email: email,
                    roles: access.roles,
                    status: 'online',
                };

                // Return a new observable with the response
                return of(response);
            })
        );
    }

    /**
     * Sign in using the access token
     */
    signInUsingToken(): Observable<any> {
        const access = this._getAccessState(this.accessToken);
        if (!access.allowed) {
            this.signOut();
            return of(false);
        }

        const email =
            (access.claims?.email as string | undefined) ||
            (access.claims?.preferred_username as string | undefined) ||
            (access.claims?.upn as string | undefined) ||
            '';

        this._authenticated = true;
        this._userService.user = {
            id: email || 'user',
            name:
                (access.claims?.name as string | undefined) ||
                (email ? email.split('@')[0] : 'Usuario'),
            email: email,
            roles: access.roles,
            status: 'online',
        };

        return of(true);
    }

    /**
     * Sign out
     */
    signOut(): Observable<any> {
        // Remove the access token from the local storage
        localStorage.removeItem('accessToken');
        localStorage.removeItem('refreshToken');

        // Set the authenticated flag to false
        this._authenticated = false;

        // Return the observable
        return of(true);
    }

    /**
     * Sign up
     *
     * @param user
     */
    signUp(user: {
        name: string;
        email: string;
        password: string;
        company: string;
    }): Observable<any> {
        return this._httpClient.post('api/auth/sign-up', user);
    }

    /**
     * Unlock session
     *
     * @param credentials
     */
    unlockSession(credentials: {
        email: string;
        password: string;
    }): Observable<any> {
        return this._httpClient.post('api/auth/unlock-session', credentials);
    }

    /**
     * Check the authentication status
     */
    check(): Observable<boolean> {
        // Check if the user is logged in
        if (this._authenticated) {
            const access = this._getAccessState(this.accessToken);
            if (access.allowed) {
                return of(true);
            }

            this.signOut();
            return of(false);
        }

        // Check the access token availability
        if (!this.accessToken) {
            return of(false);
        }

        const access = this._getAccessState(this.accessToken);
        if (!access.allowed) {
            return of(false);
        }

        // If the access token exists, and it didn't expire, sign in using it
        return this.signInUsingToken();
    }

    private _tryDecodeJwt(token: string): any | null {
        try {
            const parts = token.split('.');
            if (parts.length !== 3) {
                return null;
            }

            const base64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
            const jsonPayload = decodeURIComponent(
                atob(base64)
                    .split('')
                    .map((c) => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2))
                    .join('')
            );
            return JSON.parse(jsonPayload);
        } catch {
            return null;
        }
    }

    private _getAccessState(token: string): {
        allowed: boolean;
        claims: any | null;
        roles: string[];
    } {
        if (!token || AuthUtils.isTokenExpired(token)) {
            return { allowed: false, claims: null, roles: [] };
        }

        const claims = this._tryDecodeJwt(token);
        if (!claims) {
            return { allowed: false, claims: null, roles: [] };
        }

        const roles = this._extractRoles(claims);
        return {
            allowed: this._hasAllowedRole(roles),
            claims,
            roles,
        };
    }

    private _extractRoles(claims: any): string[] {
        const values = [
            claims?.role,
            claims?.roles,
            claims?.['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'],
            claims?.['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/role'],
        ];

        const roles: string[] = [];

        for (const value of values) {
            if (Array.isArray(value)) {
                for (const item of value) {
                    if (typeof item === 'string' && item.trim().length > 0) {
                        roles.push(item.trim());
                    }
                }
                continue;
            }

            if (typeof value !== 'string') {
                continue;
            }

            const raw = value.trim();
            if (!raw) {
                continue;
            }

            if (raw.startsWith('[') && raw.endsWith(']')) {
                try {
                    const parsed = JSON.parse(raw);
                    if (Array.isArray(parsed)) {
                        for (const item of parsed) {
                            if (typeof item === 'string' && item.trim().length > 0) {
                                roles.push(item.trim());
                            }
                        }
                        continue;
                    }
                } catch {
                    // Continue with delimiter parsing.
                }
            }

            const split = raw
                .split(/[;,]/g)
                .map((s) => s.trim())
                .filter((s) => s.length > 0);

            roles.push(...split);
        }

        return Array.from(new Set(roles));
    }

    private _hasAllowedRole(roles: string[]): boolean {
        if (!roles.length) {
            return false;
        }

        return roles.some((role) =>
            ALLOWED_ROLES.includes(role.trim().toLowerCase())
        );
    }
}

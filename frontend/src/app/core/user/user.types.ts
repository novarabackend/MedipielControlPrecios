export interface User {
    id: string;
    name: string;
    email: string;
    roles?: string[];
    avatar?: string;
    status?: string;
}

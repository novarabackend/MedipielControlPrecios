import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface SchedulerSettings {
    dailyTime: string;
    daysOfWeekMask: number;
    enabled: boolean;
    mode: string;
}

export interface SchedulerStatus {
    running: boolean;
    runningSince: string | null;
    lastRunAt: string | null;
    lastStatus: string | null;
    lastMessage: string | null;
}

const API_BASE = 'http://localhost:5000/api';

@Injectable({ providedIn: 'root' })
export class SchedulerService {
    private _http = inject(HttpClient);

    getSettings(): Observable<SchedulerSettings> {
        return this._http.get<SchedulerSettings>(`${API_BASE}/scheduler/settings`);
    }

    updateSettings(payload: {
        dailyTime: string;
        daysOfWeekMask: number;
        enabled: boolean;
    }): Observable<SchedulerSettings> {
        return this._http.put<SchedulerSettings>(`${API_BASE}/scheduler/settings`, payload);
    }

    getStatus(): Observable<SchedulerStatus> {
        return this._http.get<SchedulerStatus>(`${API_BASE}/scheduler/status`);
    }

    runManual(): Observable<{ runId: number }> {
        return this._http.post<{ runId: number }>(`${API_BASE}/scheduler/run`, {});
    }
}

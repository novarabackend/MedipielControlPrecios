import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface AlertRule {
    id: number;
    brandId: number;
    brandName: string;
    listPriceThresholdPercent: number | null;
    promoPriceThresholdPercent: number | null;
    active: boolean;
}

export interface AlertRuleUpsert {
    listPriceThresholdPercent: number | null;
    promoPriceThresholdPercent: number | null;
    active: boolean;
}

const API_BASE = 'http://localhost:5000/api';

@Injectable({ providedIn: 'root' })
export class AlertsService {
    private _http = inject(HttpClient);

    getRules(): Observable<AlertRule[]> {
        return this._http.get<AlertRule[]>(`${API_BASE}/alerts/rules`);
    }

    upsertRule(brandId: number, payload: AlertRuleUpsert): Observable<AlertRule> {
        return this._http.put<AlertRule>(
            `${API_BASE}/alerts/rules/${brandId}`,
            payload
        );
    }
}

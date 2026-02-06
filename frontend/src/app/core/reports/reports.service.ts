import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface ReportFilters {
    from?: string;
    to?: string;
    brandId?: number | null;
    categoryId?: number | null;
    productIds?: number[] | null;
    format?: 'wide' | 'long';
}

const API_BASE = 'http://localhost:5000/api';

@Injectable({ providedIn: 'root' })
export class ReportsService {
    private _http = inject(HttpClient);

    downloadExcel(filters: ReportFilters): Observable<Blob> {
        let params = new HttpParams();
        if (filters.from) {
            params = params.set('from', filters.from);
        }
        if (filters.to) {
            params = params.set('to', filters.to);
        }
        if (filters.brandId) {
            params = params.set('brandId', String(filters.brandId));
        }
        if (filters.categoryId) {
            params = params.set('categoryId', String(filters.categoryId));
        }
        if (filters.productIds && filters.productIds.length > 0) {
            params = params.set('productIds', filters.productIds.join(','));
        }
        if (filters.format) {
            params = params.set('layout', filters.format);
        }

        return this._http.get(`${API_BASE}/reports/excel`, {
            params,
            responseType: 'blob',
        });
    }
}

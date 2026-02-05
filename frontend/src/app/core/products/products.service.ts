import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface ProductItem {
    id: number;
    sku: string;
    ean: string | null;
    description: string;
    brandId: number | null;
    supplierId: number | null;
    categoryId: number | null;
    lineId: number | null;
    brandName: string | null;
    supplierName: string | null;
    categoryName: string | null;
    lineName: string | null;
    medipielListPrice: number | null;
    medipielPromoPrice: number | null;
}

export interface CompetitorInfo {
    id: number;
    name: string;
    color: string | null;
}

export interface ProductDetailInfo {
    id: number;
    sku: string | null;
    ean: string | null;
    description: string;
    brand: string | null;
    category: string | null;
    supplier: string | null;
    line: string | null;
    medipielListPrice: number | null;
    medipielPromoPrice: number | null;
}

export interface ProductCompetitorPrice {
    competitorId: number;
    listPrice: number | null;
    promoPrice: number | null;
    url: string | null;
    matchMethod: string | null;
    matchScore: number | null;
    lastMatchedAt: string | null;
    diffList: number | null;
    diffPromo: number | null;
}

export interface ProductHistoryPrice {
    competitorId: number;
    listPrice: number | null;
    promoPrice: number | null;
    diffList: number | null;
    diffPromo: number | null;
}

export interface ProductHistoryPoint {
    date: string;
    prices: ProductHistoryPrice[];
}

export interface ProductDetailResponse {
    product: ProductDetailInfo;
    snapshotDate: string | null;
    competitors: CompetitorInfo[];
    latest: ProductCompetitorPrice[];
    history: ProductHistoryPoint[];
}

export interface UpdateCompetitorUrlResponse {
    productId: number;
    competitorId: number;
    url: string;
    matchMethod: string | null;
    matchScore: number | null;
    lastMatchedAt: string | null;
}

export interface ProductImportItem {
    rowNumber: number;
    sku: string;
    ean?: string | null;
    description?: string | null;
    brand?: string | null;
    supplier?: string | null;
    category?: string | null;
    line?: string | null;
    medipielListPrice?: number | null;
    medipielPromoPrice?: number | null;
}

export interface ImportError {
    rowNumber: number;
    sku?: string | null;
    ean?: string | null;
    message: string;
}

export interface ImportSummary {
    total: number;
    created: number;
    updated: number;
    failed: number;
    errors: ImportError[];
}

const API_BASE = 'http://localhost:5000/api';

@Injectable({ providedIn: 'root' })
export class ProductsService {
    private _http = inject(HttpClient);

    getProducts(): Observable<ProductItem[]> {
        return this._http.get<ProductItem[]>(`${API_BASE}/products`);
    }

    importProducts(items: ProductImportItem[]): Observable<ImportSummary> {
        return this._http.post<ImportSummary>(`${API_BASE}/products/import`, { items });
    }

    getProductDetail(id: number, days = 7): Observable<ProductDetailResponse> {
        return this._http.get<ProductDetailResponse>(
            `${API_BASE}/products/${id}/detail`,
            {
                params: { days },
            }
        );
    }

    updateCompetitorUrl(
        productId: number,
        competitorId: number,
        url: string
    ): Observable<UpdateCompetitorUrlResponse> {
        return this._http.put<UpdateCompetitorUrlResponse>(
            `${API_BASE}/products/${productId}/competitors/${competitorId}/url`,
            { url }
        );
    }
}

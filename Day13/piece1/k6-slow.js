import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend } from 'k6/metrics';

export const options = {
    vus: 10,
    duration: '30s',
    summaryTrendStats: ['med', 'p(95)', 'p(99)', 'max'],
};

export default function () {
    const res = http.get('http://localhost:5000/api/quotes/by-author', {
        timeout: '60s',
    });

    check(res, {
        'status 200': (r) => r.status === 200,
    });
}

FROM python:3.12-slim-bookworm

RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends ca-certificates curl; \
    rm -rf /var/lib/apt/lists/*; \
    useradd --system --uid 1000 --create-home --home-dir /app cffetch

WORKDIR /app

COPY --chown=cffetch:cffetch requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY --chown=cffetch:cffetch app.py .

ENV CFFETCH_PORT=8192 \
    CFFETCH_BIND=127.0.0.1 \
    CFFETCH_IMPERSONATE=chrome136 \
    CFFETCH_PROXY= \
    PYTHONUNBUFFERED=1

USER cffetch:cffetch

EXPOSE 8192/tcp

HEALTHCHECK --interval=30s \
    --timeout=5s \
    --start-period=10s \
    --retries=3 \
    CMD curl -f -s --max-time 5 http://127.0.0.1:8192/health || exit 1

CMD ["python3", "app.py"]

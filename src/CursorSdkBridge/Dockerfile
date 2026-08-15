FROM node:22-bookworm-slim AS deps
WORKDIR /app
COPY package.json ./
RUN npm install --omit=dev

FROM node:22-bookworm-slim AS final
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 appuser \
    && useradd --uid 10001 --gid 10001 --create-home appuser
COPY --from=deps /app/node_modules ./node_modules
COPY package.json server.mjs ./
RUN chown -R appuser:appuser /app
USER appuser
ENV PORT=8090
EXPOSE 8090
HEALTHCHECK --interval=10s --timeout=3s --start-period=15s --retries=3 \
  CMD curl -f http://127.0.0.1:8090/health || exit 1
CMD ["node", "server.mjs"]

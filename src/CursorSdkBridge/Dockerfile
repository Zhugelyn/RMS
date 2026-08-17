FROM node:22-bookworm-slim AS deps
WORKDIR /app
COPY CursorSdkBridge/package.json ./
RUN npm install --omit=dev

FROM node:22-bookworm-slim AS final
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 appuser \
    && useradd --uid 10001 --gid 10001 --create-home appuser
COPY --from=deps /app/node_modules ./node_modules
COPY CursorSdkBridge/package.json CursorSdkBridge/server.mjs CursorSdkBridge/image-collect.mjs ./
COPY AgentPacks /packs
RUN mkdir -p /data/marketing-images \
    && chown -R appuser:appuser /app /packs /data/marketing-images
USER appuser
ENV PORT=8090
ENV AGENT_PACKS_ROOT=/packs
ENV RESEARCH_IMAGE_VOLUME=/data/marketing-images
EXPOSE 8090
HEALTHCHECK --interval=10s --timeout=3s --start-period=15s --retries=3 \
  CMD curl -f http://127.0.0.1:8090/health || exit 1
CMD ["node", "server.mjs"]

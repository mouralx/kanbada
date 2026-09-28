FROM docker.io/library/node:22-bookworm-slim
WORKDIR /app
COPY portal/package.json portal/package-lock.json ./
RUN npm ci
COPY portal/ .
EXPOSE 4173
CMD ["npm", "run", "dev"]

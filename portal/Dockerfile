FROM docker.io/library/node:22-bookworm-slim
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
EXPOSE 4173
CMD ["npm", "run", "dev"]

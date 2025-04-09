-- MySQL Database Schema for Scavenger Hunt Backend

CREATE DATABASE IF NOT EXISTS scavengerhunt;
USE scavengerhunt;

-- Users Table
CREATE TABLE Users (
Id INT AUTO_INCREMENT PRIMARY KEY,
Email VARCHAR(255) UNIQUE NOT NULL,
PasswordHash VARCHAR(255) NOT NULL,
TeamId INT NULL,
FOREIGN KEY (TeamId) REFERENCES Teams(Id) ON DELETE SET NULL
);

-- Groups Table
CREATE TABLE Groups (
Id INT AUTO_INCREMENT PRIMARY KEY,
Name VARCHAR(255) NOT NULL
);

-- Teams Table
CREATE TABLE Teams (
Id INT AUTO_INCREMENT PRIMARY KEY,
Name VARCHAR(255) NOT NULL,
GroupId INT NOT NULL,
FOREIGN KEY (GroupId) REFERENCES Groups(Id) ON DELETE CASCADE
);

-- Scavenger Hunt Sessions Table
CREATE TABLE ScavengerHuntSessions (
Id INT AUTO_INCREMENT PRIMARY KEY,
StartDate DATETIME NOT NULL,
EndDate DATETIME NOT NULL
);

-- Puzzles Table
CREATE TABLE Puzzles (
Id INT AUTO_INCREMENT PRIMARY KEY,
Question TEXT NOT NULL,
AnswerHash VARCHAR(255) NOT NULL,
Latitude DOUBLE NOT NULL,
Longitude DOUBLE NOT NULL,
ScavengerHuntSessionId INT NOT NULL,
FOREIGN KEY (ScavengerHuntSessionId) REFERENCES ScavengerHuntSessions(Id) ON DELETE CASCADE
);

-- Submissions Table
CREATE TABLE Submissions (
Id INT AUTO_INCREMENT PRIMARY KEY,
TeamId INT NOT NULL,
PuzzleId INT NOT NULL,
SubmissionTime DATETIME NOT NULL,
IsCorrect BOOLEAN NOT NULL,
FOREIGN KEY (TeamId) REFERENCES Teams(Id) ON DELETE CASCADE,
FOREIGN KEY (PuzzleId) REFERENCES Puzzles(Id) ON DELETE CASCADE
);

-- Leaderboard Table
CREATE TABLE Leaderboards (
Id INT AUTO_INCREMENT PRIMARY KEY,
TeamId INT NOT NULL,
Score INT NOT NULL DEFAULT 0,
FOREIGN KEY (TeamId) REFERENCES Teams(Id) ON DELETE CASCADE
);

-- Seed Default Admin User (Modify PasswordHash accordingly)
INSERT INTO Users (Email, PasswordHash) VALUES ('admin@satoshisbeachhouse.com', '9*0PrTf7KYfzBl%e');

